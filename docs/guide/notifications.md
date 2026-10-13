# Notifications

Avala tells your phone when a job needs you while you are away from its window, through ntfy or a webhook of your own.

## What it does

- **It notifies you when** a decision is left to you, a job is held, a budget or limit holds a job, a job fails or a loop ends, and, if you ask for it, when a job is ready for review.
- **Only when you are away**: a notice is sent once nobody has touched the Avala window for two minutes, and never if you answered in the app first.
- **Few messages**: notices wait thirty seconds so several become one, one per job and kind until it is resolved, and at most twenty an hour.
- **Quiet hours**: urgent notices still come through, the rest arrive in one digest afterwards.
- **Minimal by default**: what happened, the repository and the job. Detailed adds the tool and its target, redacted like the log.

## How to use it

1. Open **Settings**, **Machine**, **Notifications** and choose **Set up**. Avala creates `notifications.json` with a private, random ntfy.sh topic.
2. Install the ntfy app on your phone and subscribe to that topic.
3. Choose **Send test**: "Avala test notification" should reach every device subscribed to the topic.
4. Choose what to be told of under **Send when**, and **Minimal** or **Detailed** under **Detail**. "Exactly what is sent" shows the message for a decision.
5. For a server of your own, a token, a webhook or quiet hours, choose **Open notifications.json**.

## Settings and files

**`notifications.json`**, in the data folder:

```json
{
  "enabled": true,
  "channel": "ntfy",
  "ntfy": { "server": "https://ntfy.sh", "topic": "a-private-random-topic", "tokenEnv": "NTFY_TOKEN" },
  "events": { "decision": true, "held": true, "budget": true, "failed": true, "loopEnded": true, "review": false },
  "detail": "minimal",
  "quietHours": { "from": "22:00", "to": "07:00", "days": ["mon", "tue", "wed", "thu", "fri"] }
}
```

- `channel`: `"ntfy"` (the default) or `"webhook"`, with its section.
- `ntfy`: `server` (`https://ntfy.sh` by default), `topic` (1 to 64 letters, digits, `-` or `_`) and `tokenEnv`, the name of a variable holding an access token.
- `webhook`: `url` and `secretEnv`, the name of a variable holding a secret. Each request is signed like a trigger webhook, in `X-Avala-Signature: sha256=<hex>` with `X-Avala-Timestamp` and `X-Avala-Nonce`.
- `events`: every kind is on by default except `review`.
- `detail`: `"minimal"` (the default) or `"detailed"`.
- `quietHours`: `from` and `to` as `HH:mm`, may span midnight; `days` is every day when left out.

## Good to know

- The file names the variables of a token or secret, never the secret itself.
- Without a token, anyone who knows the topic can read what is sent: keep the random topic private, or use a server of your own.
- Addresses must be `https`, except on loopback.
- A notice is retried when the server cannot be reached, then reported failed.

## See also

- [Budgets and usage limits](budgets-and-usage-limits.md)
- [Autopilot and triggers](autopilot-and-triggers.md)
- [Privacy and usage statistics](privacy-and-usage-statistics.md)
