#!/usr/bin/env bash
# Build the image and run it the way Fargate would: read-only root, /tmp scratch, env-driven, judged by exit code.
set -uo pipefail
IMAGE=lap-report:ci
docker build -q -t "$IMAGE" . >/dev/null && echo "built $IMAGE"
run() { docker run --rm --read-only --tmpfs /tmp "$@" "$IMAGE"; }

echo; echo "== RACE_ID=indy-2026"
run -e RACE_ID=indy-2026 | tail -2; echo "exit code: ${PIPESTATUS[0]}"

echo; echo "== RACE_ID=monaco-1929 (no data)"
run -e RACE_ID=monaco-1929 | tail -1; echo "exit code: ${PIPESTATUS[0]}"

echo; echo "== dependency down on attempt 1"
run -e RACE_ID=indy-2026 -e ATTEMPT=1 -e FAIL_FIRST_ATTEMPTS=1 | tail -1; echo "exit code: ${PIPESTATUS[0]}"

echo; echo "== docker stop during a long job (what Fargate does when a task is stopped)"
docker run -d --name slow --read-only --tmpfs /tmp -e RACE_ID=indy-2026 -e WORK_DELAY_MS=60000 "$IMAGE" >/dev/null
sleep 3
docker stop -t 30 slow >/dev/null
code=$(docker wait slow 2>/dev/null || docker inspect slow --format '{{.State.ExitCode}}')
docker logs slow | tail -2
echo "exit code: $code"
docker rm slow >/dev/null
echo; echo "image size: $(docker image inspect $IMAGE --format '{{.Size}}' | awk '{printf "%.0f MB", $1/1e6}')"
