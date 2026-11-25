"""Entry point: ``python -m helix_eda`` or the ``helix-eda`` console script."""

import uvicorn


def main() -> None:
    """Run the Helix EDA service with uvicorn, listening on 0.0.0.0:8000."""
    uvicorn.run("helix_eda.app:create_app", factory=True, host="0.0.0.0", port=8000)


if __name__ == "__main__":
    main()
