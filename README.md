# S70 Stream Deck

Touch Stream Deck application for OpenView S70.

## Local configuration

Local target and PC connection files are intentionally ignored by Git:

- `TargetConnectionInfo.S70.json`
- `pc.json`
- `connections.json`

Use the example files in this repository as templates and create local copies when deploying or testing.

For the PC companion, create `pc.json` from `pc.example.json` and set `baseUrl`
to the IP printed by `Ahsoka.CS.StreamDeck.Companion`:

```json
{
  "enabled": true,
  "baseUrl": "http://<PC-IP>:5055"
}
```

If the S70 moves to another computer, update `pc.json`, rebuild/redeploy the S70
package, and verify the companion from the S70 network with:

```text
http://<PC-IP>:5055/api/health
```

## Build output

Generated folders such as `bin`, `obj`, `Platform_OpenView/publish`, and `Platform_OpenView/PackageOutput` are ignored.
