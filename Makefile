.DEFAULT_GOAL := help
.NOTPARALLEL:
.PHONY: help up stop status logs build trip matching payment check

help:
	@printf '%s\n' \
		'make up       Start the local SwiftRide stack' \
		'make trip     Start Trip and its dependencies' \
		'make matching Start Matching and its dependencies' \
		'make payment  Start Payment and its dependencies' \
		'make stop     Stop project containers and preserve volumes' \
		'make status   Show project container status' \
		'make logs     Follow recent project logs' \
		'make build    Stop services and build images sequentially' \
		'make check    Run source, backend, and frontend checks'

up:
	python3 -B scripts/check-resources.py
	docker compose stop observability
	OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait --wait-timeout 300

trip matching payment:
	python3 -B scripts/check-resources.py
	docker compose --profile "*" stop
	OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait --wait-timeout 300 $@

stop:
	docker compose --profile "*" stop

status:
	docker compose --profile observability ps --all

logs:
	docker compose --profile observability logs --tail=100 --follow

build:
	python3 -B scripts/check-architecture.py
	python3 -B scripts/check-resources.py
	docker compose --profile "*" stop
	@set -e; \
	for service in rabbitmq trip matching payment gateway web; do \
		docker compose build "$$service"; \
	done

check:
	./scripts/verify.sh
