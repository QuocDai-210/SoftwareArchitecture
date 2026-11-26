.DEFAULT_GOAL := help
.NOTPARALLEL:
.PHONY: help setup build up observe stop status logs check trip matching payment

help:
	@echo "make setup | build | up | observe | logs | status | stop | check"

setup:
	python3 -B scripts/setup.py

build: setup
	python3 -B scripts/check-resources.py
	docker compose --profile "*" stop
	@set -e; for service in rabbitmq matching trip payment gateway; do docker compose build "$$service"; done

up: setup
	python3 -B scripts/check-resources.py
	docker compose --profile observability stop observability
	docker compose up --no-build -d --wait --wait-timeout 300 gateway

observe: setup
	python3 -B scripts/check-resources.py
	OTEL_SDK_DISABLED=false docker compose --profile observability up -d --no-build --wait

trip matching payment: setup
	docker compose --profile "*" stop
	docker compose up --no-build -d --wait --wait-timeout 300 $@

stop:
	docker compose --profile "*" stop

status:
	docker compose --profile "*" ps --all

logs:
	docker compose --profile "*" logs --tail=100 --follow

check:
	./scripts/verify.sh
