# Provider Research Template

Copy this file to a provider-specific note when proposing an integration. Replace the prompts with evidence; keep unsupported or untested capabilities explicit. Do not commit raw account data.

## Product and Scope

- Provider and exact product:
- Subscription usage or API billing:
- Account types and billing/workspace scopes:
- Status: Research Only / Implemented With Fixtures / Live Verified
- Date, CLI/API version, and source commit checked:

## Authentication and Ownership

Describe the browser/official CLI flow, required permissions, stable identity fields, scope checks, credential location, renewal, cancellation, reconnect, and logout. Explain how two accounts and the user's existing CLI login remain isolated.

## Usage Contract

| Field | Source Field or Calculation | Unknown/Failure Behavior |
| --- | --- | --- |
| Stable Account Identity | To Investigate | Reject Unverified Identity |
| Billing or Workspace Scope | To Investigate | Do Not Mix Scopes |
| Window ID and Unit | To Investigate | Do Not Invent a Window |
| Usage and Allowance | To Investigate | Preserve Unknowns and Provenance |
| Refresh Time | To Investigate | Show Unavailable |
| Provider Data Time | To Investigate | Distinguish From Observation Time |
| Earned Reset Count and Expiry | To Investigate | Keep Count and Details Separate |

## Source Evidence

Link authoritative documentation and record what was verified. Identify internal endpoints, reporting delays, output-format assumptions, rate limits, and terms or access constraints that need review. Explain why monitoring does not invoke inference or other billable actions.

## Implementation and Verification

List proposed transport/mapping classes and affected dispatch/presentation paths. Link synthetic fixtures, failure and lifecycle tests, and redacted native screenshots. Record live comparison, restart, renewal, two-account isolation, and removal results separately. List remaining blockers before claiming support.

Follow the full [Provider Integration Guide](../integration.md) and [Contributor Checklist](../../CONTRIBUTING.md).
