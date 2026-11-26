# Docs

One file per topic. Each file is created by the issue that builds that part of the app; links marked *(planned)* don't
exist yet.

| Doc | What it covers |
| --- | --- |
| [architecture.md](architecture.md) | Components, design rules, auth, storage layout, messaging contracts, model artifacts, configuration, local ports, delivery |
| [local-development.md](local-development.md) | Prerequisites, starting and stopping dependencies, URLs/ports/credentials, resetting data |
| [auth-setup.md](auth-setup.md) | Local services, realm import, client settings, client secrets, bootstrapping the first admin, inviting users, re-importing the realm |
| [web.md](web.md) | Running it, layout, public pages, sign-in and the session, stack, shadcn components |
| [api.md](api.md) | Project layout, configuration, auth, running it, migrations, tests |
| [python-services.md](python-services.md) | Workspace layout, helix-core vs a service, commands, conventions, tests |
| [contracts.md](contracts.md) | Why the message contracts exist, each message with an example, ack/reject rules, how to regenerate them |
| [pipelines.md](pipelines.md) | ONNX export: input/output contract, label encoding, parity check and its tolerances, adding a converter, model metadata |
| evaluation.md | *(planned)* |
| [deployment.md](deployment.md) | Infrastructure: what Terraform creates, one-time manual steps, variables, running it locally, outputs, teardown |
| user-guide.md | *(planned)* |

When you add a doc, replace its *(planned)* entry with a link.
