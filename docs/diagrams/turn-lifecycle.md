# Turn lifecycle

Generated from `TurnLifecycle`. Run the tests with `AVALA_UPDATE_DIAGRAMS=1` to refresh it.

```mermaid
stateDiagram-v2
	state Live {
		Working
		AwaitingPermission
		AwaitingAnswer
	}
	Live --> Finished : Finish
	Live --> Interrupted : Interrupt
	Live --> Failed : Fail
	Working --> AwaitingPermission : RequestPermission
	Working --> AwaitingAnswer : AskForm
	AwaitingPermission --> Working : ResolvePermission
	AwaitingPermission --> Working : Withdraw
	AwaitingAnswer --> Working : AnswerForm
	AwaitingAnswer --> Working : Withdraw
[*] --> Working
```
