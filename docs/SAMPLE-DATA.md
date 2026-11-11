# Sample data

All names, codes and values in the bundled sample (`sample/`) and in the tests are fictional.
They do not describe any real company, health plan or person. Medicare, Medicaid and Tricare
appear only as category values (government program types), never as a payor name.

The same data is used in the testing-lifecycle and migration-lifecycle repos.

## Payors

Source: `sample/PayorContracts.Host/Services/PayorInformation.cs`. Rows are listed in name
order, which is also code order.

| ID | Name | Code | Active | Category |
|---|---|---|---|---|
| 1 | Acme Health Plans | ACM001 | yes | Commercial |
| 10 | Apex Health Inc | APX001 | yes | Commercial |
| 2 | Bluebird Health Shield | BBHS01 | yes | Commercial |
| 8 | Cedarvale Care Corporation | CDR001 | no | Medicaid |
| 3 | Citrine Valley Healthcare | CTR001 | yes | Commercial |
| 5 | Hollowbrook Health Inc | HBR001 | yes | Commercial |
| 6 | Kestrel Ridge Health | KST001 | yes | Commercial |
| 11 | Maplewood Health | MAP001 | no | Commercial |
| 15 | Meadowlark State Plan | MDW001 | yes | Medicaid |
| 13 | Mesa Senior Plan A | MESA01 | yes | Medicare |
| 14 | Mesa Senior Plan B | MESB01 | yes | Medicare |
| 7 | Moonstone Healthcare | MOO001 | yes | Medicaid |
| 12 | Tamarack Federal Plan | TMK001 | yes | Tricare |
| 4 | Unity Crest Healthcare | UCH001 | yes | Commercial |
| 9 | Willowbend Health Plans | WLW001 | yes | Medicaid |

The PayorEdit pages (`sample/PayorContracts.Web/Components/Pages/PayorEdit.razor` and
`sample/PayorContracts.Legacy/wwwroot/Pages/PayorEdit.aspx`) keep their own 6-row list with
IDs 1-6: Acme Health Plans, Bluebird Health Shield, Mesa Senior Plan A, Meadowlark State
Plan, Tamarack Federal Plan, Citrine Valley Healthcare.

## Properties the PayorList tests rely on

- Name ascending: first row Acme Health Plans (ID 1). Name descending: first row Willowbend
  Health Plans (ID 9). Code ascending: first row ACM001 (ID 1). The order is the same with
  ordinal and culture-aware comparers.
- Name filter `Mesa` matches exactly 2 rows: Mesa Senior Plan A and Mesa Senior Plan B.
- Code filter `BBHS` matches exactly 1 row: BBHS01.
- Inactive rows: IDs 8 and 11.
- Edited values used by the tests: `Acme Health Plans EDITED`, `Citrine EDITED`,
  `Unity Crest EDITED`, `Kestrel ESCAPED`.

## Contracts, plans and benefits

Source: `sample/PayorContracts.Host/Services/ContractHierarchy.cs`.

| Contract ID | Contract | Type | Plans (code) |
|---|---|---|---|
| 1 | Acme Commercial PPO | Commercial | Acme PPO Gold (ACM-PPO-G), Acme PPO Silver (ACM-PPO-S), Acme PPO Bronze (ACM-PPO-B) |
| 2 | Bluebird Government Plan | Government | Bluebird Federal Basic (BBHS-FED-B), Bluebird Federal Plus (BBHS-FED-P) |
| 3 | Unity Crest Senior Advantage | Medicare | Unity Crest SA Standard (UCH-SA-STD), Unity Crest SA Premium (UCH-SA-PRM) |
| 4 | Moonstone Managed Care | Medicaid | Moonstone Essential Care (MOO-ESS), Moonstone Family Plan (MOO-FAM), Moonstone Child Plan (MOO-CHILD) |
| 5 | Citrine Commercial HMO | Commercial | Citrine HMO Standard (CTR-HMO-S), Citrine HMO Plus (CTR-HMO-P) |

Benefit names are generic (for example `Senior Rx`).

## Changing the data

If you change names or codes, keep the properties above, or update the filter and expected
values in the testing-lifecycle module tests (`.kiro/test-project/Modules/*`, Reference and
Target files together) and in its `Harness.Tests`.
