# Local code signing

Chit-Chat uses a persistent signing identity so macOS can recognize new builds as the same
app when evaluating Accessibility and microphone permissions.

`make app` and `make install` select the **Chit Chat Local Development** identity from the
login keychain. If it is absent, they select an available Developer ID Application identity.
If neither is valid, the build stops before replacing the app. `make test` does not need a
signing identity. Distribution builds can pass `SIGN_ID` explicitly to make.

## Set up a development identity

Use Keychain Access → Certificate Assistant → Create a Certificate to create a code-signing
identity named **Chit Chat Local Development** in the login keychain. Configure trust for
code signing only. Keep both the certificate and its associated private key in the keychain.
Alternatively, install an Apple-issued Developer ID Application identity.

Use the same identity across rebuilds to retain macOS permission grants. Replacing the
certificate or switching identities requires renewing those grants. For public macOS
distribution, use an Apple-issued signing identity and notarize the build.

Check availability with:

```sh
security find-identity -v -p codesigning
make check-signing
```

## One-time permission renewal

When changing from an ad-hoc build to this identity, quit Chit-Chat and clear only its stale
Accessibility entry:

```sh
tccutil reset Accessibility ai.pivotstudio.chit-chat
```

Quit System Settings completely, then reopen it after launching Chit-Chat. Enable the app
under Privacy & Security → Accessibility. macOS may also request microphone approval again.
After that grant, verify a rebuild retains hotkey access. Keep the installed app at
`/Applications/Chit-Chat.app` and reuse the same signing certificate.
