.DEFAULT_GOAL := help
.NOTPARALLEL:
.PHONY: help up stop status logs build trip matching payment check

help:
	@printf '%s\n' \
		'make up                    Bật toàn hệ thống, đồng bộ Keycloak' \
		'make matching              Nhóm Matching (tương tự: trip, payment)' \
		'make stop                  Dừng hệ thống, giữ nguyên dữ liệu' \
		'make status                Xem trạng thái container' \
		'make logs                  Theo dõi 100 dòng log gần nhất' \
		'make build                 Dừng hệ thống, build image tuần tự' \
		'make check                 Chạy bộ kiểm tra đầy đủ'

up:
	python3 -B scripts/check-resources.py
	docker compose stop observability
	OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait --wait-timeout 300
	python3 -B scripts/sync-identity.py

trip matching payment:
	python3 -B scripts/check-resources.py
	docker compose --profile observability stop
	OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait --wait-timeout 300 $@
	python3 -B scripts/sync-identity.py

stop:
	docker compose --profile observability stop

status:
	docker compose --profile observability ps --all

logs:
	docker compose --profile observability logs --tail=100 --follow

build:
	python3 -B scripts/check-architecture.py
	python3 -B scripts/check-resources.py
	docker compose --profile observability stop
	@set -e; \
	for service in rabbitmq trip matching payment gateway web; do \
		docker compose build "$$service"; \
	done

check:
	./scripts/verify.sh
