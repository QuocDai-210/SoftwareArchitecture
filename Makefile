.DEFAULT_GOAL := help
.NOTPARALLEL:
.PHONY: help up demo stop status logs build build-demo trip matching payment check

help:
	@printf '%s\n' \
		'make up       Start the API stack for Postman' \
		'make demo     Start web, APIs and Grafana' \
		'make trip     Start Trip and its dependencies' \
		'make matching Start Matching and its dependencies' \
		'make payment  Start Payment and its dependencies' \
		'make stop     Stop project containers and preserve volumes' \
		'make status   Show project container status' \
		'make logs     Follow recent project logs' \
		'make build    Build API stack images sequentially' \
		'make build-demo Build API and web images' \
		'make check    Run source, backend, and frontend checks'

up:
	python3 -B scripts/check-resources.py
	docker compose --profile "*" stop web observability
	MAPS_PROVIDER=Test OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait \
		--wait-timeout 300 gateway

demo:
	python3 -B scripts/check-resources.py
	OTEL_SDK_DISABLED=false docker compose --profile web --profile observability up --no-build -d --wait \
		--wait-timeout 300

trip matching payment:
	python3 -B scripts/check-resources.py
	docker compose --profile "*" stop
	OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait --wait-timeout 300 $@

stop:
	docker compose --profile "*" stop

status:
	docker compose --profile "*" ps --all

logs:
	docker compose --profile "*" logs --tail=100 --follow

build-demo: build
	docker compose build web

build:
	python3 -B scripts/check-architecture.py
	python3 -B scripts/check-resources.py
	docker compose --profile "*" stop
	@set -e; \
	for service in rabbitmq trip matching payment gateway; do \
		docker compose build "$$service"; \
	done

check:
	./scripts/verify.sh
