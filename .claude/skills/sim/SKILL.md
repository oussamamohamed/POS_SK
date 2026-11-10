---
name: sim
description: Run the PosKit tests, then build and launch the RestaurantPOS iPad app in the iOS simulator and report the result in detail. Use whenever the user wants to run, launch, try, test or see the app on iPad or in the simulator, e.g. "/sim", "lance l'app", "build and run", "teste sur le simulateur", "run on iPad", "montre-moi l'app", even if they never say "simulator".
---

# Build and launch the iPad POS app

Run everything from the `ios/` directory. Goal: PosKit tests green, then a fresh build of RestaurantPOS running in the iPad simulator, then a detailed report. Run the steps in order; each one depends on the previous.

## 1. Run the PosKit tests

```bash
cd ios && swift test --package-path Packages/PosKit
```

If any test fails, **stop here**. Report each failing test (name, file:line, assertion message) and do not build: the app depends on PosKit, so launching it on top of a failing core library would hide real problems.

## 2. Discover the scheme and bundle identifier

Do not hardcode these; read them from the project so the skill keeps working when they change.

```bash
xcodebuild -list -project RestaurantPOS.xcodeproj
```

Use the app scheme (normally `RestaurantPOS`; ignore PosKit's own scheme). Once the simulator is chosen (step 3), read the bundle identifier:

```bash
xcodebuild -showBuildSettings -project RestaurantPOS.xcodeproj -scheme <scheme> \
  -destination 'platform=iOS Simulator,id=<udid>' | grep PRODUCT_BUNDLE_IDENTIFIER
```

## 3. Pick the simulator

```bash
xcrun simctl list devices available
```

Prefer `iPad Pro 13-inch (M5)`, the device `CLAUDE.md` uses. If it is not installed, fall back to another available iPad (the largest Pro first) and say which one you used in the report. If no iPad simulator exists at all, stop and tell the user to install an iPad runtime in Xcode. Work with the device UDID from here on, since names can be ambiguous across runtimes.

## 4. Build for the simulator

No code signing is needed for the simulator, so skip it. Exception: to test device pairing or discovery, build signed (drop `CODE_SIGNING_ALLOWED=NO`). Keep the full log so errors and warnings can be extracted afterwards.

```bash
set -o pipefail
mkdir -p build
xcodebuild -project RestaurantPOS.xcodeproj -scheme <scheme> \
  -destination 'platform=iOS Simulator,id=<udid>' \
  -derivedDataPath build/DerivedData \
  CODE_SIGNING_ALLOWED=NO build 2>&1 | tee build/sim-build.log
```

If the build fails, stop: report the errors as `file:line: message` and do not launch. Launching the previous build would make the user believe their latest changes are running when they are not.

## 5. Boot the simulator

```bash
xcrun simctl boot <udid>      # "already booted" is fine, ignore it
open -a Simulator
xcrun simctl bootstatus <udid> -b
```

## 6. Install and launch

```bash
xcrun simctl terminate <udid> <bundle-id>   # ignore errors; ensures the new build is the one running
xcrun simctl install <udid> build/DerivedData/Build/Products/Debug-iphonesimulator/<App>.app
xcrun simctl launch <udid> <bundle-id>
```

Find the `.app` name with `ls build/DerivedData/Build/Products/Debug-iphonesimulator/` rather than guessing.

## 7. Report

Give a detailed report in this shape:

- **Tests**: passed / failed counts, duration
- **Build**: succeeded or failed, duration; every error as `file:line: message`; warnings grouped by file with a total count (list each unique warning once)
- **Simulator**: device name, iOS version, UDID, and whether it was already booted
- **Launch**: bundle identifier and the process ID returned by `simctl launch`
- **If something failed**: the step that failed and the most likely cause, in one or two sentences
