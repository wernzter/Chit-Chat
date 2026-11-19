EXEC := ChitChat
APPNAME := Chit-Chat.app
CONFIG ?= debug
STAGE ?= $(HOME)/Library/Caches/ChitChatBuild
SCRATCH ?= $(STAGE)/scratch
BUNDLE := $(STAGE)/$(APPNAME)
CONTENTS := $(BUNDLE)/Contents
INSTALL_PATH := /Applications/$(APPNAME)
SIGN_ID ?= $(shell sh Tools/signing-identity.sh)

.PHONY: all app build check-signing clean icon install run test

all: app

build:
	swift build --scratch-path "$(SCRATCH)" --configuration $(CONFIG)

test:
	swift test --scratch-path "$(SCRATCH)"

icon:
	@swift Tools/makeicon.swift
	@iconutil --convert icns --output Resources/AppIcon.icns Resources/AppIcon.iconset
	@echo "Regenerated the macOS app icon and Windows tray icon"

check-signing:
	@case "$(SIGN_ID)" in \
		''|-) echo "Set up a persistent signing identity using docs/LOCAL-SIGNING.md." >&2; exit 1 ;; \
	esac

app: check-signing build
	@rm -rf "$(BUNDLE)"
	@install -d "$(CONTENTS)/MacOS" "$(CONTENTS)/Resources"
	@install -m 755 "$(SCRATCH)/$(CONFIG)/$(EXEC)" "$(CONTENTS)/MacOS/$(EXEC)"
	@install -m 644 Resources/Info.plist "$(CONTENTS)/Info.plist"
	@install -m 644 Resources/AppIcon.icns "$(CONTENTS)/Resources/AppIcon.icns"
	@printf 'APPL????' > "$(CONTENTS)/PkgInfo"
	@xattr -cr "$(BUNDLE)"
	@codesign --sign "$(SIGN_ID)" --force --options runtime --timestamp=none \
		--entitlements Resources/ChitChat.entitlements "$(BUNDLE)"
	@echo "Signed $(BUNDLE)"

run: app
	@pkill -x "$(EXEC)" 2>/dev/null || true
	@open "$(BUNDLE)"

install: app
	@pkill -x "$(EXEC)" 2>/dev/null || true
	@rm -rf "$(INSTALL_PATH)"
	@ditto "$(BUNDLE)" "$(INSTALL_PATH)"
	@rm -rf "$(BUNDLE)"
	@open "$(INSTALL_PATH)"

clean:
	@rm -rf "$(STAGE)" "$(SCRATCH)" .build
