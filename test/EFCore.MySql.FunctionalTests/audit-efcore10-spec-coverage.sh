#!/usr/bin/env bash

set -Eeuo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_directory="$(cd -- "$script_directory/../.." && pwd)"
compliance_file="$repository_directory/test/EFCore.MySql.FunctionalTests/MySqlComplianceTest.cs"
ledger_file="$repository_directory/docs/efcore10-spec-deferrals.md"

# This is the immutable main tip used when EF Core 10 work started. The checkout
# must retain this commit (CI should use a full-history checkout); keeping the
# comparison fixed avoids auditing all historical skips in the repository.
baseline_commit="14c2cd558e37c71e7a2fca75e339ef902effdf60"

fail() {
  printf 'EF Core 10 specification audit failed: %s\n' "$1" >&2
  exit 1
}

trim() {
  local value="$1"
  value="${value#${value%%[![:space:]]*}}"
  value="${value%${value##*[![:space:]]}}"
  printf '%s' "$value"
}

[[ -f "$compliance_file" ]] || fail "compliance source was not found: $compliance_file"
[[ -f "$ledger_file" ]] || fail "ledger was not found: $ledger_file"
git -C "$repository_directory" cat-file -e "$baseline_commit^{commit}" \
  || fail "fixed audit baseline is unavailable: $baseline_commit"

is_valid_category() {
  case "$1" in
    database-native|upstream\ issue|historically\ unsupported|explicit\ follow-up)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

# A row cannot be closed with wording alone: `coverage=` must name provider
# tests that exist in this repository (or `none`), and `deferred=` must state a
# concrete remaining scope and repeat the row's follow-up identifier.
validate_coverage() {
  local kind="$1"
  local key="$2"
  local coverage="$3"

  coverage="$(trim "$coverage")"

  if [[ "$coverage" == "none" ]]; then
    return 0
  fi

  [[ -n "$coverage" ]] || fail "missing coverage evidence for $kind $key"

  local found_reference=0
  local reference_entry

  while IFS= read -r reference_entry; do
    reference_entry="$(trim "$reference_entry")"
    [[ -n "$reference_entry" ]] || continue
    found_reference=1

    if [[ ! "$reference_entry" =~ ^test/[A-Za-z0-9_./-]+\.cs#[A-Za-z0-9_]+$ ]]; then
      fail "coverage evidence of $kind $key is not in test/<path>.cs#<Member> form: $reference_entry"
    fi

    local referenced_file="${reference_entry%%#*}"
    local referenced_member="${reference_entry##*#}"

    [[ -f "$repository_directory/$referenced_file" ]] \
      || fail "coverage evidence of $kind $key points at a missing file: $referenced_file"

    local declaration_state
    declaration_state="$(member_declaration_state "$repository_directory/$referenced_file" "$referenced_member")"

    case "$declaration_state" in
      declared)
        ;;
      disabled)
        fail "coverage evidence of $kind $key points at a disabled method in $referenced_file: $referenced_member"
        ;;
      *)
        fail "coverage evidence of $kind $key points at a member that is not declared in $referenced_file: $referenced_member"
        ;;
    esac
  done < <(printf '%s\n' "$coverage" | tr ',' '\n')

  [[ "$found_reference" == 1 ]] || fail "coverage evidence of $kind $key is empty"
}

# Resolves how a coverage reference is declared in a source file. A reference is
# only evidence when the name is declared as a type or as a method in the file: a
# name that only occurs as a substring of another name or inside a comment is not
# a declaration, and a method that is disabled by a `Skip = ...` attribute is not
# a test that runs. Line comments are removed and block comments are tracked
# across lines before the line is inspected, so text that a comment only mentions
# or comments out can never be accepted as a declaration. The check stays line
# oriented on purpose, so that it works with the text search tools available on
# macOS; it recognizes the comment and literal delimiters of C#, not the full
# grammar.
member_declaration_state() {
  local file_path="$1"
  local member="$2"

  awk -v member="$member" '
    BEGIN {
      single_quote = sprintf("%c", 39)
    }
    # Counts the double quotes that start at a position, which identifies a raw
    # string delimiter (""" ... """) and its matching closing delimiter.
    function quote_run(line, start,   count) {
      count = 0
      while (substr(line, start + count, 1) == "\"") {
        count++
      }
      return count
    }
    # Removes comments from a source line and masks the contents of string and
    # character literals, so that a comment delimiter inside a literal is not
    # read as the start of a comment. Block comments are tracked across lines
    # through in_block_comment.
    function code_text(line,   text, position, line_length, character, next_character, literal_quote) {
      text = ""
      position = 1
      line_length = length(line)
      literal_quote = ""

      while (position <= line_length) {
        character = substr(line, position, 1)
        next_character = substr(line, position + 1, 1)

        if (in_raw_string > 0) {
          if (quote_run(line, position) >= in_raw_string) {
            position += in_raw_string
            in_raw_string = 0
          } else {
            position++
          }
          continue
        }

        if (in_block_comment) {
          if (character == "*" && next_character == "/") {
            in_block_comment = 0
            position += 2
          } else {
            position++
          }
          continue
        }

        if (literal_quote != "") {
          if (character == "\\") {
            position += 2
          } else if (character == literal_quote) {
            if (next_character == literal_quote) {
              position += 2
            } else {
              literal_quote = ""
              position++
            }
          } else {
            position++
          }
          continue
        }

        if (character == "/" && next_character == "/") {
          break
        }

        if (character == "/" && next_character == "*") {
          in_block_comment = 1
          position += 2
          continue
        }

        if (character == "\"" && quote_run(line, position) >= 3) {
          in_raw_string = quote_run(line, position)
          position += in_raw_string
          continue
        }

        if (character == "\"" || character == single_quote) {
          literal_quote = character
          position++
          continue
        }

        text = text character
        position++
      }

      return text
    }
    function is_type_declaration(line,   pattern) {
      pattern = "^[[:space:]]*([A-Za-z_][A-Za-z0-9_]*[[:space:]]+)*(class|struct|interface|enum|record([[:space:]]+(class|struct))?)[[:space:]]+" member "([^A-Za-z0-9_]|$)"
      return line ~ pattern
    }
    function is_method_declaration(line,   pattern) {
      pattern = "^[[:space:]]*(public|protected|private|internal)[[:space:]]+.*[^A-Za-z0-9_]" member "[[:space:]]*\\("
      return line ~ pattern
    }
    {
      line = code_text($0)

      if (line ~ /^[[:space:]]*\[/ || in_attribute) {
        attribute_text = attribute_text " " line
        in_attribute = (line !~ /\][[:space:]]*$/)
        next
      }

      if (line ~ /^[[:space:]]*$/) {
        next
      }

      if (is_type_declaration(line)) {
        print "declared"
        exit 0
      }

      if (is_method_declaration(line)) {
        if (attribute_text ~ /Skip[[:space:]]*=/) {
          print "disabled"
        } else {
          print "declared"
        }

        exit 0
      }

      attribute_text = ""
      in_attribute = 0
    }' "$file_path"
}

# The validator is the only thing that keeps the ledger honest, so it is verified
# against source fixtures that must be rejected before the ledger is audited.
run_validator_negative_checks() {
  local fixture_directory
  fixture_directory="$(mktemp -d "${TMPDIR:-/tmp}/efcore10-audit-fixture.XXXXXX")" \
    || fail "cannot create a temporary directory for the validator negative checks"

  cat > "$fixture_directory/ReferenceFixture.cs" <<'FIXTURE'
namespace AuditFixture;

// Owned_json_comment_only_reference is only mentioned in this comment.

public class LazyLoadProxyMySqlTestDerived
{
    public void Substring_reference_method_derived()
    {
    }
}
FIXTURE

  cat > "$fixture_directory/CommentFixture.cs" <<'FIXTURE'
namespace AuditFixture;

// Block_comment_line_comment_reference is only mentioned in a line comment.

/*
public sealed class Block_comment_only_type_reference
{
    public void Block_comment_only_method_reference()
    {
    }
}
*/

/*
 * A decorated block comment still comments its content out:
 *
 *     public void Block_comment_only_indented_reference()
 *     {
 *     }
 */

/*
[ConditionalFact(Skip = "the provider does not support this yet")]
public void Block_comment_only_skip_reference()
{
}
*/
FIXTURE

  cat > "$fixture_directory/DeclarationFixture.cs" <<'FIXTURE'
namespace AuditFixture;

public class DeclarationFixture
{
    /*
     * [ConditionalFact(Skip = "the provider does not support this yet")]
     */
    public void Method_after_commented_out_skip_attribute()
    {
    }

    /* An inline block comment in front of a real declaration. */ public void Method_after_inline_block_comment()
    {
    }

    public void Method_before_comment_delimiters_in_string_literals()
    {
        var opener = "/*";
        var terminator = "//";

        AssertCommentFixture(opener, terminator);
    }

    public void Method_after_comment_delimiters_in_string_literals()
    {
    }

    public void Method_before_raw_string_with_declaration_text()
    {
        var sql = """
            public void Raw_string_declaration_reference()
            {
            }
            """;
    }

    public void Method_after_raw_string_with_declaration_text()
    {
    }

    private static void AssertCommentFixture(string opener, string terminator)
    {
    }
}
FIXTURE

  cat > "$fixture_directory/DisabledFixture.cs" <<'FIXTURE'
namespace AuditFixture;

public class DisabledCoverageTest
{
    [ConditionalFact(Skip = "the provider does not support this yet")]
    public void Disabled_coverage_method()
    {
    }

    public void Enabled_coverage_method()
    {
    }
}
FIXTURE

  cat > "$fixture_directory/DisabledCommentFixture.cs" <<'FIXTURE'
namespace AuditFixture;

public class DisabledCommentCoverageTest
{
    /* [ConditionalFact(Skip = "this commented-out skip must not disable anything")] */
    [ConditionalFact(Skip = "the provider does not support this yet")]
    public void Disabled_method_after_commented_out_skip()
    {
    }

    [ConditionalFact(Skip = "the provider does not support this yet")]
    /* A block comment between the attribute and the declaration. */
    public void Disabled_method_with_intervening_block_comment()
    {
    }
}
FIXTURE

  local state

  state="$(member_declaration_state "$fixture_directory/ReferenceFixture.cs" "LazyLoadProxyMySqlTest")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a substring of LazyLoadProxyMySqlTestDerived was accepted as a declaration"

  state="$(member_declaration_state "$fixture_directory/ReferenceFixture.cs" "Owned_json_comment_only_reference")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a name that only occurs in a comment was accepted as a declaration"

  state="$(member_declaration_state "$fixture_directory/ReferenceFixture.cs" "Substring_reference_method")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a substring of a method name was accepted as a declaration"

  state="$(member_declaration_state "$fixture_directory/CommentFixture.cs" "Block_comment_line_comment_reference")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a name that only occurs in a line comment was accepted as a declaration"

  state="$(member_declaration_state "$fixture_directory/CommentFixture.cs" "Block_comment_only_type_reference")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a type declared inside a multiline block comment was accepted as a declaration"

  state="$(member_declaration_state "$fixture_directory/CommentFixture.cs" "Block_comment_only_method_reference")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a method declared inside a multiline block comment was accepted as a declaration"

  state="$(member_declaration_state "$fixture_directory/CommentFixture.cs" "Block_comment_only_skip_reference")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a commented-out Skip attribute and method inside a block comment produced a state instead of being ignored (state=$state)"

  state="$(member_declaration_state "$fixture_directory/CommentFixture.cs" "Block_comment_only_indented_reference")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a declaration inside a decorated block comment was accepted as a declaration (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DisabledFixture.cs" "Disabled_coverage_method")"
  [[ "$state" == "disabled" ]] \
    || fail "validator negative check failed: a method that is disabled by a Skip attribute was not rejected (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DisabledCommentFixture.cs" "Disabled_method_after_commented_out_skip")"
  [[ "$state" == "disabled" ]] \
    || fail "validator negative check failed: a real Skip attribute after a commented-out one was not rejected (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DisabledCommentFixture.cs" "Disabled_method_with_intervening_block_comment")"
  [[ "$state" == "disabled" ]] \
    || fail "validator negative check failed: a block comment between a Skip attribute and its method dropped the disabled state (state=$state)"

  state="$(member_declaration_state "$fixture_directory/ReferenceFixture.cs" "LazyLoadProxyMySqlTestDerived")"
  [[ "$state" == "declared" ]] \
    || fail "validator negative check failed: a real type declaration was not accepted (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DisabledFixture.cs" "Enabled_coverage_method")"
  [[ "$state" == "declared" ]] \
    || fail "validator negative check failed: an enabled method next to a disabled one was not accepted (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DeclarationFixture.cs" "Method_after_commented_out_skip_attribute")"
  [[ "$state" == "declared" ]] \
    || fail "validator negative check failed: a real method after a commented-out Skip attribute was not accepted (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DeclarationFixture.cs" "Method_after_inline_block_comment")"
  [[ "$state" == "declared" ]] \
    || fail "validator negative check failed: a real method after an inline block comment was not accepted (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DeclarationFixture.cs" "Method_after_comment_delimiters_in_string_literals")"
  [[ "$state" == "declared" ]] \
    || fail "validator negative check failed: a real method after comment delimiters inside string literals was not accepted (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DeclarationFixture.cs" "Raw_string_declaration_reference")"
  [[ -z "$state" ]] \
    || fail "validator negative check failed: a declaration-shaped line inside a raw string literal was accepted as a declaration (state=$state)"

  state="$(member_declaration_state "$fixture_directory/DeclarationFixture.cs" "Method_after_raw_string_with_declaration_text")"
  [[ "$state" == "declared" ]] \
    || fail "validator negative check failed: a real method after a raw string literal was not accepted (state=$state)"

  rm -rf "$fixture_directory"

  printf 'Validator negative checks passed: substring, line-comment, block-comment and disabled declarations are rejected.\n'
}

validate_deferred() {
  local kind="$1"
  local key="$2"
  local reference="$3"
  local deferred="$4"
  local coverage="$5"

  deferred="$(trim "$deferred")"

  [[ -n "$deferred" ]] || fail "missing deferred scope for $kind $key"

  local deferred_word_count
  deferred_word_count="$(printf '%s\n' "$deferred" | awk '{ print NF }')"
  [[ "$deferred_word_count" -ge 8 ]] \
    || fail "deferred scope of $kind $key is too vague to audit: $deferred"

  if printf '%s' "$deferred" | grep -E -i -q 'needs reconciliation|where supported|as appropriate|as needed|\btbd\b|\btodo\b|\bn/a\b|unclear|not applicable|\betc\.'; then
    fail "deferred scope of $kind $key only rephrases the deferral: $deferred"
  fi

  grep -F -q -- "$reference" <<< "$deferred" \
    || fail "deferred scope of $kind $key does not repeat its follow-up identifier: $reference"

  if [[ "$(trim "$coverage")" == "none" ]]; then
    grep -F -q -- "uncovered" <<< "$deferred" \
      || fail "deferred scope of $kind $key must state that the behavior area is uncovered"
  fi
}

run_validator_negative_checks

seen_deferred_scopes=()

# The table is deliberately machine-readable, but the document remains the
# source of the explanation and evidence for each entry.
while IFS=$'\t' read -r kind key category owner reference coverage deferred; do
  kind="$(trim "${kind:-}")"

  [[ -n "$key" ]] || fail "ledger row has no stable source key"
  is_valid_category "$category" || fail "invalid category for $kind $key: $category"
  [[ -n "$owner" && "$owner" != "-" ]] || fail "missing owner for $kind $key"
  [[ -n "$reference" && "$reference" != "-" ]] || fail "missing follow-up/reference for $kind $key"

  validate_coverage "$kind" "$key" "$coverage"
  validate_deferred "$kind" "$key" "$reference" "$deferred" "$coverage"

  for seen_scope in ${seen_deferred_scopes[@]+"${seen_deferred_scopes[@]}"}; do
    [[ "$seen_scope" != "$(trim "$deferred")" ]] \
      || fail "two ledger rows share the same deferred scope wording: $kind $key"
  done

  seen_deferred_scopes+=("$(trim "$deferred")")
done < <(awk -F'|' '
  {
    kind_value = $2
    gsub(/^[[:space:]]+|[[:space:]]+$/, "", kind_value)
    if (kind_value != "base" && kind_value != "method") {
      next
    }
    if (NF < 8) {
      print kind_value "\tMALFORMED\t\t\t\t"
      next
    }
    for (field_index = 2; field_index < NF; field_index++) {
      value = $field_index
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", value)
      row[field_index - 1] = value
    }
    evidence_value = row[6]
    deferred_value = ""
    coverage_value = ""
    if (match(evidence_value, /coverage=/)) {
      coverage_value = substr(evidence_value, RSTART + 9)
      gsub(/;.*$/, "", coverage_value)
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", coverage_value)
    }
    if (match(evidence_value, /deferred=/)) {
      deferred_value = substr(evidence_value, RSTART + 9)
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", deferred_value)
    }
    print row[1] "\t" row[2] "\t" row[3] "\t" row[4] "\t" row[5] "\t" coverage_value "\t" deferred_value
  }' "$ledger_file")

base_types_text="$(awk '
  /EF10-SPEC-DEFERRALS/ { in_block = 1 }
  in_block && /typeof\(/ {
    value = $0
    sub(/^.*typeof\(/, "", value)
    sub(/\).*/, "", value)
    print value
  }
  in_block && /};/ { exit }
' "$compliance_file")"

base_type_count="$(printf '%s\n' "$base_types_text" | awk 'NF { count++ } END { print count + 0 }')"
[[ "$base_type_count" == 39 ]] || fail "expected 39 EF Core 10 base ignores, found $base_type_count"

while IFS= read -r base_type; do
  [[ -n "$base_type" ]] || continue
  row_count="$(grep -F -c "| base | $base_type |" "$ledger_file" || true)"
  [[ "$row_count" == 1 ]] || fail "base ignore is missing or duplicated in the ledger: $base_type"
done <<< "$base_types_text"

extract_ignored_types() {
  awk '
    function emit_type(value) {
      sub(/^[[:space:]]+/, "", value)
      sub(/[[:space:]]+$/, "", value)
      if (value != "") {
        print value
      }
    }
    function scan(line,    start, closing_position) {
      sub(/\/\/.*$/, "", line)
      while (1) {
        if (in_type) {
          closing_position = index(line, ")")
          if (closing_position == 0) {
            type_value = type_value line
            return
          }
          emit_type(type_value substr(line, 1, closing_position - 1))
          in_type = 0
          type_value = ""
          line = substr(line, closing_position + 1)
        }

        start = index(line, "typeof(")
        if (start == 0) {
          return
        }
        line = substr(line, start + 7)
        closing_position = index(line, ")")
        if (closing_position == 0) {
          in_type = 1
          type_value = line
          return
        }
        emit_type(substr(line, 1, closing_position - 1))
        line = substr(line, closing_position + 1)
      }
    }
    /IgnoredTestBases/ && /new HashSet<Type>/ {
      in_initializer = 1
      next
    }
    in_initializer {
      scan($0)
      if (!in_type && $0 ~ /^[[:space:]]*};/) {
        exit
      }
    }
  '
}

baseline_ignored_types="$(git -C "$repository_directory" show "$baseline_commit:test/EFCore.MySql.FunctionalTests/MySqlComplianceTest.cs" | extract_ignored_types | sort -u)"
current_ignored_types="$(extract_ignored_types < "$compliance_file" | sort -u)"
added_ignored_types="$(comm -13 <(printf '%s\n' "$baseline_ignored_types") <(printf '%s\n' "$current_ignored_types") || true)"

while IFS= read -r added_type; do
  [[ -n "$added_type" ]] || continue
  row_count="$(grep -F -c "| base | $added_type |" "$ledger_file" || true)"
  [[ "$row_count" == 1 ]] || fail "new ignored base is missing or duplicated in the ledger: $added_type"
done <<< "$added_ignored_types"

expected_method_keys='test/EFCore.MySql.FunctionalTests/LazyLoadProxyMySqlTest.cs#Top_level_projection_track_entities_before_passing_to_client_method()
test/EFCore.MySql.FunctionalTests/MigrationsInfrastructureMySqlTest.cs#Can_apply_two_migrations_in_transaction_async()
test/EFCore.MySql.FunctionalTests/MigrationsMySqlTest.cs#Convert_string_column_to_a_json_column_containing_collection()
test/EFCore.MySql.FunctionalTests/MigrationsMySqlTest.cs#Convert_string_column_to_a_json_column_containing_reference()
test/EFCore.MySql.FunctionalTests/MigrationsMySqlTest.cs#Multiop_rename_table_and_create_new_table_with_the_old_name()
test/EFCore.MySql.FunctionalTests/MigrationsMySqlTest.cs#Multiop_rename_table_and_drop()
test/EFCore.MySql.FunctionalTests/MigrationsMySqlTest.cs#Rename_table_with_json_column()
test/EFCore.MySql.FunctionalTests/Query/AdHocQuerySplittingQueryMySqlTest.cs#Can_query_with_nav_collection_in_projection_with_split_query_in_parallel_async()
test/EFCore.MySql.FunctionalTests/Query/AdHocQuerySplittingQueryMySqlTest.cs#Can_query_with_nav_collection_in_projection_with_split_query_in_parallel_sync()
test/EFCore.MySql.FunctionalTests/Query/FromSqlQueryMySqlTest.cs#Multiple_occurrences_of_FromSql_with_db_parameter_adds_two_parameters(bool async)
test/EFCore.MySql.FunctionalTests/Query/NonSharedPrimitiveCollectionsQueryMySqlTest.cs#Column_collection_inside_json_owned_entity()
test/EFCore.MySql.FunctionalTests/Query/NorthwindMiscellaneousQueryMySqlTest.cs#Where_nanosecond_and_microsecond_component(bool async)
test/EFCore.MySql.FunctionalTests/Query/PrimitiveCollectionsQueryMySqlTest.cs#Inline_collection_index_Column_with_EF_Constant()
test/EFCore.MySql.FunctionalTests/Query/SqlQueryMySqlTest.cs#Multiple_occurrences_of_SqlQuery_with_db_parameter_adds_two_parameters(bool async)'

extract_skipped_methods() {
  local include_line="${1:-0}"

  awk -v include_line="$include_line" '
    function emit_method(declaration,    open_position, close_position, method_name, parameters, signature) {
      sub(/^.*\][[:space:]]*/, "", declaration)
      sub(/^[[:space:]]*(public|protected|private|internal)[[:space:]]*/, "", declaration)
      open_position = index(declaration, "(")
      if (open_position == 0) {
        return
      }
      method_name = substr(declaration, 1, open_position - 1)
      sub(/^.*[[:space:]]/, "", method_name)
      parameters = substr(declaration, open_position + 1)
      close_position = index(parameters, ")")
      if (close_position > 0) {
        parameters = substr(parameters, 1, close_position - 1)
      }
      gsub(/[[:space:]]+/, " ", parameters)
      sub(/^[[:space:]]+/, "", parameters)
      sub(/[[:space:]]+$/, "", parameters)
      signature = method_name "(" parameters ")"
      if (include_line) {
        print skip_line "\t" signature
      } else {
        print signature
      }
    }
    /Skip[[:space:]]*=/ {
      pending = 1
      skip_line = FNR
      if ($0 ~ /\][[:space:]]*(public|protected|private|internal)[[:space:]]/) {
        emit_method($0)
        pending = 0
      }
      next
    }
    pending && /^[[:space:]]*(public|protected|private|internal)[[:space:]]/ {
      emit_method($0)
      pending = 0
      next
    }
    /^[[:space:]]*(public|protected|private|internal)[[:space:]]/ { pending = 0 }
  '
}

extract_added_skip_lines() {
  local file_path="$1"

  # Signature keys catch a Skip moved to another overload. A persistent Skip
  # whose EF10 override signature evolved is ignored unless its attribute line
  # was added, which keeps API evolution distinct from a newly moved deferral.
  git -C "$repository_directory" diff --unified=0 "$baseline_commit" -- "$file_path" |
    awk '
      /^@@/ {
        header = $0
        sub(/^.*\+/, "", header)
        sub(/,.*/, "", header)
        current_line = header + 0
        next
      }
      /^\+\+\+/ { next }
      /^\+/ {
        if ($0 ~ /Skip[[:space:]]*=/) {
          print current_line
        }
        current_line++
        next
      }
      /^-/ { next }
      { current_line++ }
    '
}

current_added_method_keys=""
while IFS= read -r file_path; do
  [[ -f "$repository_directory/$file_path" ]] || continue
  baseline_methods="$(git -C "$repository_directory" show "$baseline_commit:$file_path" 2>/dev/null | extract_skipped_methods | sort || true)"
  current_methods_with_lines="$(extract_skipped_methods include-line < "$repository_directory/$file_path" | sort || true)"
  current_methods="$(printf '%s\n' "$current_methods_with_lines" | cut -f2- | sort)"
  added_skip_lines="$(extract_added_skip_lines "$file_path" || true)"
  added_methods="$(comm -13 <(printf '%s\n' "$baseline_methods") <(printf '%s\n' "$current_methods") || true)"

  while IFS= read -r method_signature; do
    [[ -n "$method_signature" ]] || continue
    method_name="${method_signature%%(*}"
    baseline_has_same_name="$(printf '%s\n' "$baseline_methods" | awk -v method_name="$method_name" '
      {
        signature = $0
        sub(/\(.*/, "", signature)
        if (signature == method_name) {
          found = 1
        }
      }
      END { print found + 0 }
    ')"
    if [[ "$baseline_has_same_name" == 1 ]]; then
      current_skip_was_added=0
      while IFS=$'\t' read -r skip_line current_signature; do
        [[ "$current_signature" == "$method_signature" ]] || continue
        if printf '%s\n' "$added_skip_lines" | grep -F -x -q "$skip_line"; then
          current_skip_was_added=1
          break
        fi
      done <<< "$current_methods_with_lines"
      [[ "$current_skip_was_added" == 1 ]] || continue
    fi
    method_key="$file_path#$method_signature"
    current_added_method_keys+="$method_key\n"
    row_count="$(grep -F -c "| method | $method_key |" "$ledger_file" || true)"
    [[ "$row_count" == 1 ]] || fail "new method-level skip is missing or duplicated in the ledger: $method_key"
  done <<< "$added_methods"
done < <(git -C "$repository_directory" diff --name-only "$baseline_commit" -- test/EFCore.MySql.FunctionalTests | grep -E '\.cs$')

expected_method_count="$(printf '%s\n' "$expected_method_keys" | awk 'NF { count++ } END { print count + 0 }')"
actual_method_count="$(printf '%b' "$current_added_method_keys" | awk 'NF { count++ } END { print count + 0 }')"
[[ "$actual_method_count" == "$expected_method_count" ]] \
  || fail "fixed-baseline method skip inventory changed: expected $expected_method_count, found $actual_method_count"

while IFS= read -r method_key; do
  [[ -n "$method_key" ]] || continue
  printf '%b' "$current_added_method_keys" | grep -F -x -q "$method_key" \
    || fail "expected method-level skip is not present in the fixed-baseline diff: $method_key"
done <<< "$expected_method_keys"

ledger_base_count="$(grep -F -c '| base |' "$ledger_file" || true)"
ledger_method_count="$(grep -F -c '| method |' "$ledger_file" || true)"
[[ "$ledger_base_count" == 39 ]] || fail "ledger must contain 39 base rows, found $ledger_base_count"
[[ "$ledger_method_count" == 14 ]] || fail "ledger must contain 14 method rows, found $ledger_method_count"

printf 'EF Core 10 specification audit passed: 39 base ignores and 14 method-level skips are classified.\n'
