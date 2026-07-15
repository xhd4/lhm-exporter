SHELL := /bin/sh

TARGETRID ?= win-x64
DIST_DIR := dist/$(TARGETRID)
CONFIG ?= Release
SERVICE_NAME ?= lhm-exporter
SERVICE_NAME := $(strip $(SERVICE_NAME))
SERVICE_EXE ?= lhm-exporter.exe

DOCKER_BUILD = docker build \
	--target artifact \
	--build-arg TARGETRID=$(TARGETRID) \
	--build-arg CONFIG=$(CONFIG) \
	--output type=local,dest=./$(DIST_DIR) \
	.

# Cross-platform commands
ifeq ($(OS),Windows_NT)
	MKDIR_CMD := - mkdir $(DIST_DIR)
	STOP_SERVICE_CMD := powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(CURDIR)/scripts/stop-service.ps1" -ServiceName "$(SERVICE_NAME)" -DistDir "$(CURDIR)/$(DIST_DIR)" -ExeName "$(SERVICE_EXE)"
	START_SERVICE_CMD := powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(CURDIR)/scripts/start-service.ps1" -ServiceName "$(SERVICE_NAME)" -DistDir "$(CURDIR)/$(DIST_DIR)" -ExeName "$(SERVICE_EXE)"
	CLEAN_RID_CMD := powershell.exe -NoProfile -Command "if (Test-Path '$(CURDIR)/$(DIST_DIR)') { Remove-Item -Recurse -Force '$(CURDIR)/$(DIST_DIR)' -ErrorAction SilentlyContinue }"
	CLEAN_TMP_CMD := powershell.exe -NoProfile -Command "if (Test-Path '$(CURDIR)/$(DIST_DIR)') { Get-ChildItem '$(CURDIR)/$(DIST_DIR)' -Filter '.tmp*' -File | Remove-Item -Force -ErrorAction SilentlyContinue }"
	CLEAN_BUILD_CMD := powershell.exe -NoProfile -Command "$$p = Join-Path $$env:TEMP 'lhm-exporter'; if (Test-Path $$p) { Remove-Item -Recurse -Force $$p -ErrorAction SilentlyContinue }"
else
	MKDIR_CMD := mkdir -p $(DIST_DIR)
	STOP_SERVICE_CMD := @echo "skip stop-service-win (non-Windows)"
	START_SERVICE_CMD := @echo "skip start-service-win (non-Windows)"
	CLEAN_RID_CMD := rm -rf $(DIST_DIR)
	CLEAN_TMP_CMD := rm -f $(DIST_DIR)/.tmp* $(DIST_DIR)/*.tmp*
	CLEAN_BUILD_CMD := rm -rf /tmp/lhm-exporter
endif

.PHONY: docker-win docker-win-x64 docker-win-arm64 clean clean-tmp stop-service-win start-service-win

stop-service-win:
	$(STOP_SERVICE_CMD)

start-service-win:
	$(START_SERVICE_CMD)

# Stop service, clean dist/RID, build, restart service (dev workflow on Windows)
docker-win: stop-service-win clean-rid
	$(MKDIR_CMD)
	$(DOCKER_BUILD) || { $(MAKE) clean-tmp; exit 1; }
	$(MAKE) start-service-win

docker-win-x64:
	$(MAKE) docker-win TARGETRID=win-x64

docker-win-arm64:
	$(MAKE) docker-win TARGETRID=win-arm64

clean-rid:
	- $(CLEAN_RID_CMD)

clean-tmp:
	- $(CLEAN_TMP_CMD)

clean:
	- rm -rf dist
	- $(CLEAN_BUILD_CMD)
