# ODM Design Principles

## Swiss Army Knife of ONVIF

ODM's mission is to be the swiss army knife of ONVIF — it must work with non-compliant, broken, and partial ONVIF implementations on a best-effort basis. Never drop a camera because something in its ONVIF support doesn't follow the spec completely.

ODM routinely encounters cameras with broken ONVIF support. Its value proposition is tolerance — attempt every fallback, accept partial implementations, never give up.

### ONVIF Device Initialization — Escalating Fallback Chain

When connecting to any camera, follow this sequence. Never stop at the first failure — always try the next level:

1. **Parse Profile T scope** from WS-Discovery → early Media2 hint, but do not rely on it exclusively (non-compliant cameras may support Media2 without advertising Profile T)
2. **`GetServices()`** → preferred source for all service xAddrs (spec-compliant path)
3. **`GetCapabilities()`** → fallback if GetServices fails or returns incomplete data
4. **Hardcoded URL construction** → last resort (e.g. append `/onvif/media_service` to device base URL) for cameras that implement neither
5. **Try Media2 first** if any signal suggests it; fall back to Media1 on any failure
6. **Never rewrite host:port** unless the advertised address is unroutable (loopback / any-address) — in that case substitute the known device IP but leave the port untouched
7. **Per-call retry on raw xAddr** if the adjusted URL fails

Do NOT remove fallbacks in the name of simplicity. Do NOT assume any single ONVIF call will succeed. Do NOT drop cameras that fail the spec.
