# Helix Reborn

Helix Reborn is a hosted, invite-only, no-code web app for building, training and evaluating machine learning models on tabular data. It is the successor to the original [Helix](https://github.com/Biomaterials-for-Medical-Devices-AI/Helix) Streamlit app.

## Status

This project is in early development and is not ready for use.

## What it will do

- Sign in (invite only)
- Create an experiment and upload a dataset
- Explore the data (EDA)
- Build a training pipeline (preprocessing + model) and run it
- Evaluate the model (metrics, plots, feature importance)
- Use the model to predict on new data, and download models (ONNX), plots and data

## Stack

- Next.js (web)
- ASP.NET Core (API)
- PostgreSQL
- Keycloak (auth)
- Python services (EDA, training, evaluation) using scikit-learn and ONNX
- RabbitMQ
- DigitalOcean (hosting, with Terraform)

## Design

See [docs/DESIGN.md](docs/DESIGN.md) for the full design document.

Architecture and conventions: [docs/architecture.md](docs/architecture.md).

## License

Licensed under the MIT License — see [LICENSE](LICENSE) for details.
