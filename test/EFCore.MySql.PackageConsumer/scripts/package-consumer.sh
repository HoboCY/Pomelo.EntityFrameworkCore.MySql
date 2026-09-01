#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_directory/../../.." && pwd)"
consumer_project="$repository_root/test/EFCore.MySql.PackageConsumer/EFCore.MySql.PackageConsumer.csproj"
consumer_nuget_config="$repository_root/test/EFCore.MySql.PackageConsumer/NuGet.config"
package_output="${PACKAGE_OUTPUT:-$repository_root/artifacts/packages}"
provider_version="${POMELO_PACKAGE_VERSION:-10.0.0-rtm.3}"
efcore_floor_version="${EFCORE_FLOOR_VERSION:-10.0.0}"
mysql_image="${MYSQL_IMAGE:-mysql:8.4.3}"
mariadb_image="${MARIADB_IMAGE:-mariadb:11.6.2}"
database_password="${DATABASE_PASSWORD:-Password12!}"
dotnet_working_directory="${DOTNET_WORKING_DIRECTORY:-$repository_root}"
consumer_packages_directory="$(mktemp -d "${TMPDIR:-/tmp}/pomelo-efcore10-package-consumer.XXXXXX")"
containers=()
database_port=

cleanup() {
    if ((${#containers[@]} > 0)); then
        for container in "${containers[@]}"; do
            docker rm --force "$container" >/dev/null 2>&1 || true
        done
    fi

    if [[ -d "$consumer_packages_directory" ]]; then
        rm -rf -- "$consumer_packages_directory"
        printf 'Removed isolated consumer NuGet packages: %s\n' "$consumer_packages_directory" >&2
    fi
}

trap cleanup EXIT

printf 'Using isolated consumer NuGet packages: %s\n' "$consumer_packages_directory" >&2

run_dotnet() {
    (
        cd "$dotnet_working_directory"
        dotnet "$@"
    )
}

run_consumer_dotnet() {
    (
        cd "$dotnet_working_directory"
        NUGET_PACKAGES="$consumer_packages_directory" dotnet "$@"
    )
}

source_projects=(
    "$repository_root/src/EFCore.MySql/EFCore.MySql.csproj"
    "$repository_root/src/EFCore.MySql.NTS/EFCore.MySql.NTS.csproj"
    "$repository_root/src/EFCore.MySql.Json.Microsoft/EFCore.MySql.Json.Microsoft.csproj"
    "$repository_root/src/EFCore.MySql.Json.Newtonsoft/EFCore.MySql.Json.Newtonsoft.csproj"
)

if [[ "${SKIP_PACKAGE:-false}" != "true" ]]; then
    mkdir -p "$package_output"

    # Restore and build with the floor first. The package metadata is regenerated after a
    # range restore below, while --no-build keeps these floor-compiled binaries intact.
    for project in "${source_projects[@]}"; do
        run_dotnet restore "$project" \
            --no-cache \
            -p:EFCoreVersion="$efcore_floor_version" \
            -p:Version="$provider_version"
    done

    for configuration in Debug Release; do
        for project in "${source_projects[@]}"; do
            run_dotnet build "$project" \
                -c "$configuration" \
                --no-restore \
                -p:EFCoreVersion="$efcore_floor_version" \
                -p:Version="$provider_version"
        done
    done

    # A range restore makes the generated nuspec retain the declared compatibility range. The
    # no-build pack then packages the binaries compiled against the 10.0.0 floor above.
    for project in "${source_projects[@]}"; do
        run_dotnet restore "$project" \
            --no-cache \
            -p:Version="$provider_version"
    done

    for project in "${source_projects[@]}"; do
        run_dotnet pack "$project" \
            -c Release \
            -o "$package_output" \
            --no-restore \
            --no-build \
            -p:Version="$provider_version"
    done

    provider_package="$package_output/Pomelo.EntityFrameworkCore.MySql.$provider_version.nupkg"
    provider_nuspec="$(unzip -p "$provider_package" '*.nuspec')"
    if [[ "$provider_nuspec" != *'version="[10.0.0, 10.0.999]"'* ]]; then
        printf 'The provider package does not declare EF Core [10.0.0,10.0.999].\n' >&2
        exit 1
    fi

    printf 'Provider package declares EF Core [10.0.0,10.0.999].\n'
fi

start_database() {
    local database_type="$1"
    local image="$2"
    local client
    local container_name="pomelo-efcore10-package-consumer-${database_type}-$$"
    local database_name="pomelo_package_consumer_${database_type}"
    local host_port
    local attempt

    if [[ "$database_type" == "mysql" ]]; then
        client=mysql
        docker run \
            --name "$container_name" \
            --env MYSQL_ROOT_PASSWORD="$database_password" \
            --env MYSQL_DATABASE="$database_name" \
            --publish 127.0.0.1::3306 \
            --detach \
            "$image" >/dev/null
    else
        client=mariadb
        docker run \
            --name "$container_name" \
            --env MARIADB_ROOT_PASSWORD="$database_password" \
            --env MARIADB_DATABASE="$database_name" \
            --publish 127.0.0.1::3306 \
            --detach \
            "$image" >/dev/null
    fi

    containers+=("$container_name")
    host_port="$(docker port "$container_name" 3306/tcp | sed -n 's/.*://p' | head -n 1)"

    for attempt in $(seq 1 120); do
        if docker exec "$container_name" "$client" \
            --protocol=socket \
            --user=root \
            --password="$database_password" \
            --execute='SELECT 1' >/dev/null 2>&1; then
            printf '%s database ready: image=%s port=%s attempts=%s\n' "$database_type" "$image" "$host_port" "$attempt" >&2
            database_port="$host_port"
            return 0
        fi

        sleep 1
    done

    docker logs "$container_name"
    return 1
}

run_consumer() {
    local database_type="$1"
    local efcore_version="$2"
    local image="$3"
    local port
    local connection_string

    start_database "$database_type" "$image"
    port="$database_port"
    connection_string="Server=127.0.0.1;Port=$port;User ID=root;Password=$database_password;Database=pomelo_package_consumer_${database_type};"

    run_consumer_dotnet restore "$consumer_project" \
        --configfile "$consumer_nuget_config" \
        --no-cache \
        -p:ConsumerProviderVersion="$provider_version" \
        -p:ConsumerEfCoreVersion="$efcore_version"
    run_consumer_dotnet build "$consumer_project" \
        -c Release \
        --no-restore \
        -p:ConsumerProviderVersion="$provider_version" \
        -p:ConsumerEfCoreVersion="$efcore_version"

    POMELO_PACKAGE_CONSUMER_CONNECTION_STRING="$connection_string" \
    POMELO_PACKAGE_CONSUMER_SERVER_TYPE="$database_type" \
        run_consumer_dotnet run \
            --project "$consumer_project" \
            -c Release \
            --no-restore \
            --no-build \
            -p:ConsumerProviderVersion="$provider_version" \
            -p:ConsumerEfCoreVersion="$efcore_version"
}

run_consumer mysql "$efcore_floor_version" "$mysql_image"
run_consumer mariadb "$efcore_floor_version" "$mariadb_image"

printf 'Package consumer validation passed for EF Core %s on MySQL and MariaDB.\n' "$efcore_floor_version"
