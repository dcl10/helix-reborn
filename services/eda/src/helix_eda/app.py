"""The Helix EDA FastAPI application."""

from typing import Annotated

from fastapi import APIRouter, Depends, FastAPI, Request
from fastapi.responses import JSONResponse

from helix_core.datasets import load_csv
from helix_core.errors import InvalidDataError, InvalidSpecError, StorageError
from helix_core.logging import configure_logging
from helix_core.settings import get_settings
from helix_core.storage import Storage
from helix_eda.dependencies import get_storage, require_service_token
from helix_eda.routes_plots import router as plots_router
from helix_eda.schemas import DatasetSummary, SummaryRequest
from helix_eda.summary import summarise

router = APIRouter(dependencies=[Depends(require_service_token)])


@router.post("/summary", response_model=DatasetSummary)
def post_summary(
    body: SummaryRequest, storage: Annotated[Storage, Depends(get_storage)]
) -> DatasetSummary:
    """Summarise a CSV dataset read from S3.

    Args:
        body: The request body, naming the dataset's S3 key.
        storage: The storage client to read the dataset from.

    Returns:
        Row/column counts and per-column summary statistics.

    Raises:
        InvalidDataError: If the object isn't readable as a non-empty CSV.
        StorageError: If the object doesn't exist in S3.
    """
    df = load_csv(storage, body.dataset_key)
    return summarise(df)


def create_app() -> FastAPI:
    """Build the Helix EDA FastAPI application.

    Configures JSON logging, registers exception handlers for the shared
    ``helix_core.errors`` types, and mounts every route but ``/health`` behind
    service-token authentication.

    Returns:
        The configured :class:`FastAPI` application.
    """
    settings = get_settings()
    configure_logging(level=settings.log_level, service="eda")

    app = FastAPI(title="Helix EDA", version=settings.version)

    @app.get("/health")
    def health() -> dict[str, str]:
        """Report service health. Not behind authentication.

        Returns:
            ``{"status": "ok", "version": "<version>"}``.
        """
        return {"status": "ok", "version": settings.version}

    @app.exception_handler(InvalidDataError)
    @app.exception_handler(InvalidSpecError)
    def handle_invalid(_: Request, exc: Exception) -> JSONResponse:
        return JSONResponse(status_code=422, content={"detail": str(exc)})

    @app.exception_handler(StorageError)
    def handle_storage_error(_: Request, exc: StorageError) -> JSONResponse:
        return JSONResponse(status_code=404, content={"detail": str(exc)})

    app.include_router(router)
    app.include_router(plots_router)
    return app
