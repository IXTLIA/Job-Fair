# Job Fair schema 2 campaign contract

Schema 1 is unchanged: `starter-jobs-sha256-v1`, the frozen four starter job
selection and the original six save fields. It is a legacy starter-only run,
never implicitly upgraded. `NextRunConfig.ParseSeed` remains schema-1-only.

Schema 2 records the entire placement table, not a recipe to recreate it on load.
`RunMetadata`, `RunSettings` and `RewardPlacement` are immutable. Renaming and
successful claims return a new snapshot with the same run ID and placement hash.
Loading does not consult the launcher or invoke the Python generator.

As of 0.6.0 there are three supported native Mixed catalog identities:

| Catalog | Treasure scope | Ordinary placements | Shop slots |
|---|---|---:|---:|
| `mixed-native-jobs-wind-treasure-v1` | `wind-shrine-v1` | 4 | 0 |
| `mixed-native-jobs-world-treasure-v2` | `world-pools-v2` | 260 | 0 |
| `mixed-world-treasure-shops-v3` | `world-pools-v2` | 260 | 836 |

Each additionally has21 native job placements. Settings normalize by catalog,
not just mode. Old saves and loaded launcher profiles retain the entire v1
table. The v2 validator compares item/gil/quantity multisets separately for each
world and counts map-state aliases once. The approved Aegis variant and first
world ownership are frozen in v2; future scope changes require another catalog
identity. v3 composes the frozen v2 rewards with a new shop catalog; its settings
add `shop_scope: world-type-pools-v1` and `shop_prices: native-item-prices`.
Shop stock preserves each world/type's multiset and forbids duplicates within
one shop. Old catalogs omit these fields and retain native shops. Remixed is
still rejected by the native-playability validator. All supported catalogs retain
Moore's native either/or weapon event and the twelve fixed tablet weapons.

## Launcher request

`NextRunConfig.Parse(json)` accepts up to262,144 characters for v3 (65,536 for
v1/v2 campaigns and4,096 for schema1 seed requests) and returns a
`NextRunRequest`. Its `CreateRun(name)` assigns a new run ID, copies the validated
table and starts with no completed claims. Schema 1 requests also work and remain
schema 1. All objects reject missing, unknown and duplicate fields.

A schema 2 request has exactly these fields (the placeholder table/hash below
must be replaced by the complete generated table and its digest):

```json
{
  "schema_version": 2,
  "seed": "8",
  "generator": "job-fair-native-solver-v1",
  "mode": "Mixed Crystals",
  "settings": {
    "job_rewards": "native-crystals",
    "key_items": "vanilla",
    "treasure": "randomized",
    "treasure_scope": "wind-shrine-v1",
    "world_access": "story"
  },
  "catalog_id": "mixed-native-jobs-wind-treasure-v1",
  "starting_job_ids": [],
  "placements": [],
  "placement_hash": "<lowercase SHA-256 of complete table and configuration>"
}
```

Each placement has exactly `location_id`, `reward_kind`, `reward_id`, `quantity`.
Job/item IDs are canonical decimal **strings**; job IDs are 2–22. Job/key quantity
is one. Item quantity is 1–99. Gil uses an empty reward ID and quantity 1–9,999,999.
Empty uses an empty ID and quantity zero. Shop uses a canonical positive decimal
content ID string and quantity one. Key IDs are independent logical tokens.
Location/key tokens accept ASCII letters, digits, `-`, `_`, `.`, `/`, `:` and are
1–200 characters. No battle/repeatable reward type is implied by this contract.

Mixed has zero starters and exactly one placement for each of the 21 jobs, with
no shuffled key reward. Remixed has four distinct starters and exactly 17 other
job rewards. All locations must be distinct. The native integration additionally
validates exact location membership, source hashes and reward eligibility against
its pinned catalog; structural contract checks do not prove native access.

## Save fields and claims

The `__job_fair` object contains the request fields plus `run_id`, `bartz_name`,
`claimed_location_ids`, and `acquired_key_ids`. Both arrays are required, including
when empty. Claims must reference actual placements and cannot repeat. Acquired
keys must equal the set of key rewards at completed claims. Native flags are not
used as shuffled-key possession. `WithClaim(locationId)` must be called only after
the reward succeeds. It is idempotent for an existing claim and rejects unknown
locations. Shop slots cannot be claimed: they describe renewable stock, with
native spell ownership and purchase limits enforced during buying. Acquisition
UI and native reward dispatch are separate runtime duties.

## Placement hash

SHA-256 hashes the following UTF-8 text, with LF line endings and a final LF. All
numbers use canonical invariant decimal. There is no BOM. Placements are sorted
by ordinal ASCII location ID; starter order is preserved.

```text
JobFair|placements|1
seed|{seed}
generator|{generator}
mode|{mode}
catalog|{catalog_id}
settings|{job_rewards}|{key_items}|{treasure}|{treasure_scope}|{world_access}
starters|{comma-separated starting_job_ids}
placement|{location_id}|{reward_kind}|{reward_id}|{quantity}
...one placement line per location...
```

For v3, insert `shops|{shop_scope}|{shop_prices}` after the settings line
and before the starters line. The hash text for v1/v2 remains byte-identical.

The seed, settings, mode, generator, catalog and full rewards are bound together.
The run ID, name and evolving claim/key state are excluded. This checksum detects
configuration/table disagreement; it is not a security signature. A valid digest
never substitutes for checking bindings against the installed native catalog.

## Supported versions and Remixed boundary

Only `job-fair-native-solver-v1` is accepted for schema 2. The supported native
catalogs are the three Mixed identities listed above. A different
generator, unknown catalog, or catalog/mode mismatch is rejected explicitly.
Future releases must deliberately retain an old catalog/handler or reject it;
they must not regenerate a loaded table with newer logic.

For pure serialization tests, `remixed-contract-fixture-v1` records normalized
Remixed settings: `container-jobs`, `randomized`, `randomized`,
`contract-fixture-v1`, `key-gated`. It is **not a native campaign catalog**.
`NextRunConfig.Parse` rejects it for New Game. The native load integration must
call `CampaignContract.ValidatePlayable(metadata)` before adopting a loaded run;
this rejects the Remixed fixture. Keeping this explicit gate allows codec tests
for both schemas/modes without advertising unimplemented Remixed playability.

Portable verification: `tests/run_codec_probe.ps1` retains the legacy 87 checks;
`tests/run_campaign_probe.ps1` verifies both mode contracts, full-table persistence,
claims/keys, identity stability, strict versions, corruption and launcher parsing.
Neither test reads or writes actual game saves or establishes engine acceptance.
