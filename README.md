# VoidLedger

A Windows desktop app for tracking Warframe relic values. Pulls relic drop tables and live market prices, then shows you which relics are worth cracking.

## Features

- Live relic drop data from [Warframe Drop Data](https://drops.warframestat.us/)
- Real-time item pricing from [Warframe Market](https://warframe.market/)
- Local caching so you're not re-pulling data every launch
- Mark relics as owned, filter by vaulted/unvaulted, search by name
- "Squad" mode - select up to 4 relics to compare side by side

## Notes

This is an unofficial, fan-made tool and isn't affiliated with Digital Extremes or Warframe Market & Warframe Drop Data. It's a light API consumer - pricing pulls are rate-limited (2 req/sec) to be a good citizen of the Warframe Market API, at the cost of it taking a couple minutes.