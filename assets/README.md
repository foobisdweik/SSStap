# Assets — Runtime Data

Config templates, routing rules, and Unbound templates. These are copied to the build output at compile time.

| Path | Purpose |
|------|---------|
| **config/** | proxylist.json, config.ini (and optional dns.ini, localhost.ini) |
| **rules/** | China-IP-only.rules, Skip-all-China-IP.rules, game-specific rules |
| **unbound/** | template-service.conf, forward-zone/template.china-list.conf |
