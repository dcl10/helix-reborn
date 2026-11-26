"""Routes for the EDA service's plot endpoints.

Each endpoint loads the dataset, renders a plot with a pure function from
:mod:`helix_eda.plots`, uploads it to S3 and returns its key and metadata. Route
functions are plain ``def`` (not ``async def``): plotting is CPU-bound, so FastAPI
runs them in its threadpool rather than blocking the event loop.
"""

from typing import Annotated

from fastapi import APIRouter, Depends
from matplotlib.figure import Figure

from helix_core.datasets import load_csv
from helix_core.messages import PlotOptions
from helix_core.plotting import save_figure
from helix_core.storage import Storage
from helix_eda.dependencies import get_storage, require_service_token
from helix_eda.plots import (
    create_pairplot,
    plot_correlation_heatmap,
    plot_target_variable_distribution,
)
from helix_eda.schemas import (
    CorrelationHeatmapRequest,
    PairplotRequest,
    PlotRequest,
    PlotResponse,
    TargetDistributionRequest,
)

router = APIRouter(prefix="/plots", dependencies=[Depends(require_service_token)])


def _upload_plot(
    storage: Storage, body: PlotRequest, fig: Figure, title: str
) -> PlotResponse:
    """Upload a rendered plot to S3 and build its response.

    Args:
        storage: The storage client to write the plot to.
        body: The request the plot was rendered for, naming the output key and
            styling options.
        fig: The rendered figure. Closed as a side effect of uploading it.
        title: The plot's title, as rendered.

    Returns:
        The uploaded plot's key, content type, size and title.
    """
    dpi = (body.plot_options or PlotOptions()).dpi
    size_bytes = save_figure(storage, body.output_key, fig, dpi)
    return PlotResponse(key=body.output_key, size_bytes=size_bytes, title=title)


@router.post("/target-distribution", response_model=PlotResponse)
def post_target_distribution(
    body: TargetDistributionRequest, storage: Annotated[Storage, Depends(get_storage)]
) -> PlotResponse:
    """Plot the distribution of a target column and upload it to S3.

    Args:
        body: The request body, naming the dataset, target column and output key.
        storage: The storage client to read the dataset from and write the plot to.

    Returns:
        The uploaded plot's key, content type, size and title.
    """
    df = load_csv(storage, body.dataset_key)
    fig, title = plot_target_variable_distribution(df, body)
    return _upload_plot(storage, body, fig, title)


@router.post("/correlation-heatmap", response_model=PlotResponse)
def post_correlation_heatmap(
    body: CorrelationHeatmapRequest, storage: Annotated[Storage, Depends(get_storage)]
) -> PlotResponse:
    """Plot a correlation heatmap and upload it to S3.

    Args:
        body: The request body, naming the dataset, columns and output key.
        storage: The storage client to read the dataset from and write the plot to.

    Returns:
        The uploaded plot's key, content type, size and title.
    """
    df = load_csv(storage, body.dataset_key)
    fig, title = plot_correlation_heatmap(df, body)
    return _upload_plot(storage, body, fig, title)


@router.post("/pairplot", response_model=PlotResponse)
def post_pairplot(
    body: PairplotRequest, storage: Annotated[Storage, Depends(get_storage)]
) -> PlotResponse:
    """Plot a pairplot of the requested columns and upload it to S3.

    Args:
        body: The request body, naming the dataset, columns and output key.
        storage: The storage client to read the dataset from and write the plot to.

    Returns:
        The uploaded plot's key, content type, size and title.
    """
    df = load_csv(storage, body.dataset_key)
    fig, title = create_pairplot(df, body)
    return _upload_plot(storage, body, fig, title)
