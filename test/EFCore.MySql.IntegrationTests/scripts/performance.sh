#!/usr/bin/env bash

set -Eeuo pipefail

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
project_directory=$(cd -- "$script_directory/.." && pwd)
project_file="$project_directory/EFCore.MySql.IntegrationTests.csproj"
configuration=${CONFIGURATION:-Release}

if [[ ! -f "$project_directory/config.json" ]]; then
  echo "Missing $project_directory/config.json" >&2
  exit 1
fi

output_file=$(mktemp "${TMPDIR:-/tmp}/pomelo-performance.XXXXXX")
multi_iteration_output_file=$(mktemp "${TMPDIR:-/tmp}/pomelo-performance-multi.XXXXXX")
trap 'rm -f -- "$output_file" "$multi_iteration_output_file"' EXIT

run_cli() {
  dotnet run \
    --project "$project_file" \
    --configuration "$configuration" \
    --no-build \
    --no-restore \
    --no-launch-profile \
    -- "$@"
}

dotnet build "$project_file" --configuration "$configuration"

if ! run_cli testPerformance 1 1 100 >"$output_file" 2>&1; then
  cat "$output_file" >&2
  echo "testPerformance benchmark failed" >&2
  exit 1
fi

cat "$output_file"

for expected_output in \
  'Warmup iterations:[[:space:]]+1' \
  'Measured iterations:[[:space:]]+1' \
  'Times \(Min, Median, Average, Max\)' \
  'Managed allocation deltas \(Min, Median, Average, Max\)' \
  'Retained managed heap after measured iteration \(Min, Median, Average, Max\)'; do
  if ! grep -Eq -- "$expected_output" "$output_file"; then
    echo "Missing benchmark output matching: $expected_output" >&2
    exit 1
  fi
done

select100_count=$(awk '
  /^Test:[[:space:]]+Select 100$/ { in_select100 = 1; next }
  in_select100 && /^Records Selected:[[:space:]]+/ {
    sub(/^Records Selected:[[:space:]]*/, "")
    print
    exit
  }
' "$output_file")

if [[ "$select100_count" != 100 ]]; then
  echo "Expected Select 100 to report 100 records, got: ${select100_count:-<missing>}" >&2
  exit 1
fi

if [[ $(grep -Fxc -- "Records Inserted: 100" "$output_file") -ne 2 ||
  $(grep -Fxc -- "Records Updated: 100" "$output_file") -ne 2 ||
  $(grep -Fxc -- "Records Selected: 100" "$output_file") -ne 2 ||
  $(grep -Fxc -- "Total Sleep Commands: 100" "$output_file") -ne 1 ]]; then
  echo "Measured functional counts were contaminated by warmup or did not match the requested operations." >&2
  exit 1
fi

if ! run_cli testPerformance 2 2 3 >"$multi_iteration_output_file" 2>&1; then
  cat "$multi_iteration_output_file" >&2
  echo "Concurrent multi-iteration testPerformance benchmark failed" >&2
  exit 1
fi

for expected_output in \
  'Warmup iterations:[[:space:]]+1' \
  'Measured iterations:[[:space:]]+2' \
  'Concurrency:[[:space:]]+2' \
  'Times \(Min, Median, Average, Max\)' \
  'Managed allocation deltas \(Min, Median, Average, Max\)' \
  'Retained managed heap after measured iteration \(Min, Median, Average, Max\)'; do
  if ! grep -Eq -- "$expected_output" "$multi_iteration_output_file"; then
    echo "Missing concurrent benchmark output matching: $expected_output" >&2
    exit 1
  fi
done

for expected_count in \
  "Records Inserted: 12" \
  "Records Updated: 12" \
  "Records Selected: 12" \
  "Total Sleep Commands: 12" \
  "Records Inserted: 400" \
  "Records Updated: 400" \
  "Records Selected: 400"; do
  if [[ $(grep -Fxc -- "$expected_count" "$multi_iteration_output_file") -ne 1 ]]; then
    echo "Unexpected measured count in concurrent benchmark: $expected_count" >&2
    exit 1
  fi
done

for invalid_case in \
  "0 1 1" \
  "-1 1 1" \
  "1 0 1" \
  "1 1 0" \
  "abc 1 1" \
  "1 abc 1" \
  "1 1 abc"; do
  read -r invalid_iterations invalid_concurrency invalid_operations <<< "$invalid_case"

  if run_cli testPerformance "$invalid_iterations" "$invalid_concurrency" "$invalid_operations" >"$output_file" 2>&1; then
    echo "Expected testPerformance $invalid_case to fail" >&2
    exit 1
  fi

  if ! grep -Fq -- "must be positive" "$output_file"; then
    cat "$output_file" >&2
    echo "Invalid testPerformance $invalid_case did not report a positive-value error" >&2
    exit 1
  fi
done

echo "Public testPerformance CLI checks passed."
