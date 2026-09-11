// Copyright (c) Pomelo Foundation. All rights reserved.
// Licensed under the MIT. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;

namespace Pomelo.EntityFrameworkCore.MySql.Storage.Internal
{
    /// <summary>
    /// Helper methods to generate MySQL and MariaDB JSON path expressions.
    /// </summary>
    public static class MySqlJsonPathHelper
    {
        /// <summary>
        /// The resolved JSON paths of the JSON column modifications that are currently being generated.
        /// A JSON column modification carries neither the column nor the entity type it is mapped to, and
        /// <see cref="Microsoft.EntityFrameworkCore.Update.UpdateSqlGenerator.AppendUpdateColumnValue"/> does not
        /// receive the modification command, so the JSON paths resolved from the model in
        /// <see cref="BeginUpdateJsonPathScope"/> are scoped here until the update SQL of the command is complete.
        /// The table holds weak references to the modifications, so entries that are not explicitly removed are
        /// still released with the modification they belong to.
        /// </summary>
        private static readonly ConditionalWeakTable<IColumnModification, string> _updateJsonPaths = new();

        /// <summary>
        /// Returns the JSON path representation of a single property (member) name.
        /// A name that cannot be used unquoted (for example a name that starts with a digit, or a name that
        /// contains a dot, a double quote, a backslash or a control character) is quoted and escaped, because
        /// the server would otherwise reject the path or interpret the name as multiple path segments.
        /// Note that escaping a property name for a JSON path is not the same as escaping a SQL literal.
        /// </summary>
        public static string EscapePropertyName(string propertyName)
        {
            if (IsUnquotedPropertyName(propertyName))
            {
                return propertyName;
            }

            var builder = new StringBuilder(propertyName.Length + 2);
            builder.Append('"');

            foreach (var character in propertyName)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;

                    case '"':
                        builder.Append("\\\"");
                        break;

                    default:
                        if (char.IsControl(character))
                        {
                            // A control character must not be used literally in a JSON path, because MySQL
                            // rejects such a path and MariaDB silently matches nothing.
                            builder
                                .Append("\\u")
                                .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');

            return builder.ToString();
        }

        /// <summary>
        /// Resolves the JSON paths of all JSON column modifications of the given command from the model and makes
        /// them available via <see cref="TryGetUpdateJsonPath"/> until the returned scope is disposed.
        /// </summary>
        /// <remarks>
        /// EF Core joins the JSON names of an update path with dots, which is ambiguous for JSON names that
        /// contain a dot themselves. Only the model knows whether such a name is a single path segment.
        /// </remarks>
        public static IDisposable BeginUpdateJsonPathScope(IReadOnlyModificationCommand command)
        {
            var scope = new UpdateJsonPathScope();

            foreach (var columnModification in command.ColumnModifications)
            {
                var jsonPath = TryRebuildUpdateJsonPath(command, columnModification);

                if (jsonPath is not null)
                {
                    _updateJsonPaths.AddOrUpdate(columnModification, jsonPath);
                    scope.Modifications.Add(columnModification);
                }
            }

            return scope;
        }

        /// <summary>
        /// Returns the JSON path of a JSON column modification that was resolved from the model in the current
        /// <see cref="BeginUpdateJsonPathScope"/> scope, if any.
        /// </summary>
        public static bool TryGetUpdateJsonPath(IColumnModification columnModification, out string jsonPath)
            => _updateJsonPaths.TryGetValue(columnModification, out jsonPath);

        private static string TryRebuildUpdateJsonPath(
            IReadOnlyModificationCommand command,
            IColumnModification columnModification)
        {
            var jsonPath = columnModification.JsonPath;

            if (string.IsNullOrEmpty(jsonPath) ||
                jsonPath[0] != '$')
            {
                return null;
            }

            // A modification of a single property carries the property metadata it belongs to, which
            // identifies the exact path segment. That identity must be used instead of looking the segment
            // up by its (ambiguous) JSON name, because a JSON name can be the complete path of another
            // member (e.g. a property named "Nested.First" next to an owned type "Nested" with member
            // "First"), in which case no name-based lookup can tell the two apart.
            if (columnModification.Property is { } property)
            {
                var entityType = FindRootJsonEntityType(command, columnModification);

                return entityType is null
                    ? null
                    : TryRebuildJsonPathFromProperty(entityType, property, jsonPath);
            }

            // A modification without a property always carries a complete JSON document, which can only be
            // written to a navigation or a complex property. Which navigation that is has to be resolved
            // from the entries of the command: the path of the modification is built from their JSON names,
            // and a JSON name that contains a dot (e.g. an owned type named "Inner.Leaf" next to an owned
            // type "Inner" with member "Leaf") makes the path ambiguous for a name-based lookup.
            var entryPath = TryRebuildJsonPathFromEntries(command, columnModification, jsonPath);
            if (entryPath is not null)
            {
                return entryPath;
            }

            // The entries cannot resolve the path when the command does not contain entities that are mapped
            // to this JSON document (e.g. when a JSON collection was emptied by the update, or when a complex
            // type is stored in the column), so the names are looked up in the model as a fallback.
            return TryRebuildJsonPathFromNames(FindRootJsonEntityType(command, columnModification), jsonPath);
        }

        /// <summary>
        /// Rebuilds the JSON path of a single property modification from the property metadata, walking the
        /// containment hierarchy from the property up to the root of the JSON document.
        /// </summary>
        private static string TryRebuildJsonPathFromProperty(
            IEntityType rootEntityType,
            IProperty property,
            string jsonPath)
        {
            var propertyNames = TryResolvePropertyPathNames(rootEntityType, property);

            return propertyNames is null
                ? null
                : TryRebuildJsonPath(jsonPath, propertyNames, allowPartialPath: false);
        }

        /// <summary>
        /// Resolves the JSON names of the containment hierarchy from the root of the JSON document down to the
        /// given property, walking from the property up to the root entity type of the document.
        /// </summary>
        private static List<string> TryResolvePropertyPathNames(
            IEntityType rootEntityType,
            IProperty property)
        {
            var propertyNames = new List<string> { property.GetJsonPropertyName() };
            var declaringType = property.DeclaringType;

            while (declaringType is not null && declaringType != rootEntityType)
            {
                string jsonName;
                ITypeBase parentType;

                if (declaringType is IEntityType entityType &&
                    entityType.FindOwnership() is { } ownership)
                {
                    jsonName = entityType.GetJsonPropertyName();
                    parentType = ownership.PrincipalEntityType;
                }
                else if (declaringType is IComplexType complexType &&
                         complexType.ComplexProperty is { } complexProperty)
                {
                    jsonName = complexType.GetJsonPropertyName();
                    parentType = complexProperty.DeclaringType;
                }
                else
                {
                    return null;
                }

                if (string.IsNullOrEmpty(jsonName))
                {
                    return null;
                }

                propertyNames.Insert(0, jsonName);
                declaringType = parentType;
            }

            return declaringType is null
                ? null
                : propertyNames;
        }

        /// <summary>
        /// Rebuilds the JSON path of a whole-document modification from the entries of the command that are
        /// mapped to the JSON document of the modified column.
        /// </summary>
        private static string TryRebuildJsonPathFromEntries(
            IReadOnlyModificationCommand command,
            IColumnModification columnModification,
            string jsonPath)
        {
            var propertyNames = TryResolveEntryPathNames(command, columnModification);

            return propertyNames is null
                ? null
                : TryRebuildJsonPath(jsonPath, propertyNames, allowPartialPath: true);
        }

        /// <summary>
        /// Resolves the JSON names of the containment steps from the root of the JSON document down to every
        /// JSON entity that is part of the given command and returns the names that all of them have in common.
        /// EF Core builds the update path of the container column from those entries and trims it at the common
        /// ancestor when the update covers more than one of them, so the common names identify the navigation
        /// that the modification belongs to.
        /// </summary>
        private static List<string> TryResolveEntryPathNames(
            IReadOnlyModificationCommand command,
            IColumnModification columnModification)
        {
            List<string> commonPropertyNames = null;

            foreach (var entry in command.Entries)
            {
                var entityType = entry.EntityType;

                if (!entityType.IsMappedToJson())
                {
                    continue;
                }

                var rootEntityType = entityType;
                while (rootEntityType.FindOwnership() is { } rootOwnership &&
                       rootOwnership.PrincipalEntityType.IsMappedToJson())
                {
                    rootEntityType = rootOwnership.PrincipalEntityType;
                }

                if (!string.Equals(
                        rootEntityType.GetContainerColumnName(),
                        columnModification.ColumnName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var propertyNames = new List<string>();

                for (var containedEntityType = entityType;
                     containedEntityType != rootEntityType;)
                {
                    if (containedEntityType.FindOwnership() is not { } ownership)
                    {
                        return null;
                    }

                    propertyNames.Insert(0, containedEntityType.GetJsonPropertyName());
                    containedEntityType = ownership.PrincipalEntityType;
                }

                if (commonPropertyNames is null)
                {
                    commonPropertyNames = propertyNames;
                    continue;
                }

                var commonLength = 0;
                while (commonLength < commonPropertyNames.Count &&
                       commonLength < propertyNames.Count &&
                       string.Equals(
                           commonPropertyNames[commonLength],
                           propertyNames[commonLength],
                           StringComparison.Ordinal))
                {
                    commonLength++;
                }

                commonPropertyNames.RemoveRange(commonLength, commonPropertyNames.Count - commonLength);
            }

            return commonPropertyNames;
        }

        /// <summary>
        /// Rebuilds a JSON path from the given model-resolved property names by consuming the raw path that
        /// EF Core built from the same names. The raw path is only used for the element positions of JSON
        /// collections, because an element position is part of the identity of a collection element and not
        /// of its metadata. The names are always the ones resolved from the model, so a JSON name that
        /// contains a dot cannot be confused with a nested member.
        /// </summary>
        private static string TryRebuildJsonPath(
            string jsonPath,
            IReadOnlyList<string> propertyNames,
            bool allowPartialPath)
        {
            var remainingPath = jsonPath.AsSpan(1);
            var builder = new StringBuilder("$");

            // The root of a JSON document is written as '$'. When the document is stored as a JSON array
            // (an owned collection at the root of the document), the position of the modified element
            // follows it directly.
            if (!TryConsumeElementPosition(ref remainingPath, builder))
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                if (remainingPath.Length == 0)
                {
                    // EF Core trims the path of an update that covers multiple JSON entities at their common
                    // ancestor, which is a prefix of the names resolved from the model.
                    return allowPartialPath
                        ? builder.ToString()
                        : null;
                }

                if (remainingPath[0] != '.')
                {
                    return null;
                }

                remainingPath = remainingPath[1..];

                if (!remainingPath.StartsWith(propertyName.AsSpan(), StringComparison.Ordinal))
                {
                    return null;
                }

                remainingPath = remainingPath[propertyName.Length..];

                builder
                    .Append('.')
                    .Append(EscapePropertyName(propertyName));

                if (!TryConsumeElementPosition(ref remainingPath, builder))
                {
                    return null;
                }
            }

            return remainingPath.Length == 0
                ? builder.ToString()
                : null;
        }

        /// <summary>
        /// Consumes the element position of a JSON array from the raw path, if the raw path contains one at
        /// the current position.
        /// </summary>
        private static bool TryConsumeElementPosition(ref ReadOnlySpan<char> remainingPath, StringBuilder builder)
        {
            if (remainingPath.Length == 0 ||
                remainingPath[0] != '[')
            {
                return true;
            }

            var closingBracketPosition = remainingPath.IndexOf(']');
            if (closingBracketPosition < 0 ||
                !int.TryParse(
                    remainingPath[1..closingBracketPosition],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ordinal))
            {
                return false;
            }

            builder
                .Append('[')
                .Append(ordinal.ToString(CultureInfo.InvariantCulture))
                .Append(']');

            remainingPath = remainingPath[(closingBracketPosition + 1)..];

            return true;
        }

        /// <summary>
        /// Rebuilds a JSON path by looking the path segments up in the model by their JSON names, which is only
        /// unambiguous when no JSON name is the complete path of another member.
        /// </summary>
        private static string TryRebuildJsonPathFromNames(
            IEntityType entityType,
            string jsonPath)
        {
            if (entityType is null)
            {
                return null;
            }

            var segments = TryResolvePathSegments(entityType, jsonPath);
            if (segments is null)
            {
                return null;
            }

            var builder = new StringBuilder("$");

            foreach (var (propertyName, ordinal) in segments)
            {
                if (propertyName is not null)
                {
                    builder
                        .Append('.')
                        .Append(EscapePropertyName(propertyName));
                }

                if (ordinal is not null)
                {
                    builder
                        .Append('[')
                        .Append(ordinal.Value.ToString(CultureInfo.InvariantCulture))
                        .Append(']');
                }
            }

            return builder.ToString();
        }

        private static List<(string PropertyName, int? Ordinal)> TryResolvePathSegments(
            IEntityType rootEntityType,
            string jsonPath)
        {
            var segments = new List<(string PropertyName, int? Ordinal)>();
            ITypeBase typeBase = rootEntityType;
            var remainingPath = jsonPath.AsSpan(1);

            while (remainingPath.Length > 0)
            {
                if (remainingPath[0] == '.')
                {
                    remainingPath = remainingPath[1..];

                    var (propertyName, targetTypeBase) = FindPropertyName(typeBase, remainingPath);
                    if (propertyName is null)
                    {
                        return null;
                    }

                    segments.Add((propertyName, null));
                    remainingPath = remainingPath[propertyName.Length..];

                    // Only a navigation or complex property is followed by further path segments.
                    typeBase = targetTypeBase;
                    if (typeBase is null &&
                        remainingPath.Length > 0)
                    {
                        return null;
                    }
                }
                else if (remainingPath[0] == '[')
                {
                    var closingBracketPosition = remainingPath.IndexOf(']');
                    if (closingBracketPosition < 0 ||
                        !int.TryParse(
                            remainingPath[1..closingBracketPosition],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var ordinal))
                    {
                        return null;
                    }

                    segments.Add((null, ordinal));
                    remainingPath = remainingPath[(closingBracketPosition + 1)..];
                }
                else
                {
                    return null;
                }
            }

            return segments;
        }

        private static IEntityType FindRootJsonEntityType(
            IReadOnlyModificationCommand command,
            IColumnModification columnModification)
        {
            foreach (var entry in command.Entries)
            {
                if (!entry.EntityType.IsMappedToJson())
                {
                    continue;
                }

                var rootEntityType = entry.EntityType;
                while (rootEntityType.FindOwnership() is { } ownership &&
                       ownership.PrincipalEntityType.IsMappedToJson())
                {
                    rootEntityType = ownership.PrincipalEntityType;
                }

                if (string.Equals(
                        rootEntityType.GetContainerColumnName(),
                        columnModification.ColumnName,
                        StringComparison.Ordinal))
                {
                    return rootEntityType;
                }
            }

            return null;
        }

        private static (string PropertyName, ITypeBase TargetTypeBase) FindPropertyName(
            ITypeBase typeBase,
            ReadOnlySpan<char> remainingPath)
        {
            string propertyName = null;
            ITypeBase targetTypeBase = null;

            // Complex types have no navigations (only entity types do), so only entity types are
            // considered for navigations here.
            if (typeBase is IEntityType entityType)
            {
                foreach (var navigation in entityType.GetNavigations())
                {
                    if (!navigation.TargetEntityType.IsMappedToJson())
                    {
                        continue;
                    }

                    var jsonPropertyName = navigation.TargetEntityType.GetJsonPropertyName();
                    if (MatchesPathSegment(jsonPropertyName, remainingPath) &&
                        (propertyName is null || jsonPropertyName.Length > propertyName.Length))
                    {
                        propertyName = jsonPropertyName;
                        targetTypeBase = navigation.TargetEntityType;
                    }
                }
            }

            foreach (var complexProperty in typeBase.GetComplexProperties())
            {
                var complexType = complexProperty.ComplexType;
                if (!complexType.IsMappedToJson())
                {
                    continue;
                }

                var jsonPropertyName = complexType.GetJsonPropertyName();
                if (MatchesPathSegment(jsonPropertyName, remainingPath) &&
                    (propertyName is null || jsonPropertyName.Length > propertyName.Length))
                {
                    propertyName = jsonPropertyName;
                    targetTypeBase = complexType;
                }
            }

            return (propertyName, targetTypeBase);
        }

        private static bool MatchesPathSegment(string propertyName, ReadOnlySpan<char> remainingPath)
            => !string.IsNullOrEmpty(propertyName) &&
               remainingPath.Length >= propertyName.Length &&
               remainingPath.StartsWith(propertyName.AsSpan()) &&
               (remainingPath.Length == propertyName.Length ||
                remainingPath[propertyName.Length] is '.' or '[');

        private static bool IsUnquotedPropertyName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) ||
                !(char.IsLetter(propertyName[0]) ||
                  propertyName[0] == '_' ||
                  propertyName[0] == '$'))
            {
                // A name that starts with a digit (or with another non-letter) must be quoted, because
                // MySQL rejects it as an unquoted JSON path segment.
                return false;
            }

            foreach (var c in propertyName)
            {
                if (!char.IsLetter(c) &&
                    (c < '0' || c > '9') &&
                    c != '_' &&
                    c != '$')
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class UpdateJsonPathScope : IDisposable
        {
            public List<IColumnModification> Modifications { get; } = new();

            public void Dispose()
            {
                foreach (var modification in Modifications)
                {
                    _updateJsonPaths.Remove(modification);
                }
            }
        }
    }
}
