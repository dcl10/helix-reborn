"""Pure plotting functions for the EDA service's plot endpoints.

Ported from the original Helix's ``helix/services/plotting.py``, dropping all
Streamlit code. Each function takes the loaded dataset and its request model and
returns a ``(Figure, title)`` pair; routing, validation and upload to S3 live in
:mod:`helix_eda.routes_plots`.
"""

import numpy as np
import pandas as pd
import seaborn as sns
from matplotlib.figure import Figure

from helix_core.datasets import numeric_columns
from helix_core.errors import InvalidSpecError
from helix_core.messages import PlotOptions
from helix_core.plotting import apply_style, finish_axes, use_headless_backend
from helix_eda.schemas import (
    CorrelationHeatmapRequest,
    PairplotRequest,
    TargetDistributionRequest,
)

use_headless_backend()

_MAX_HEATMAP_COLUMNS = 50
_MAX_ANNOTATED_HEATMAP_COLUMNS = 20
_PAIRPLOT_SAMPLE_ROWS = 2000
_PAIRPLOT_SAMPLE_RANDOM_STATE = 42


def plot_target_variable_distribution(
    df: pd.DataFrame, request: TargetDistributionRequest
) -> tuple[Figure, str]:
    """Plot the distribution of a dataset's target column.

    A numeric target gets a histogram (with an optional KDE curve); a non-numeric
    target gets a count bar chart of its distinct values.

    Args:
        df: The dataset to plot.
        request: The request, naming ``target_column`` and the histogram options.

    Returns:
        The rendered figure and its title.

    Raises:
        InvalidSpecError: If ``target_column`` doesn't exist in ``df``.
    """
    if request.target_column not in df.columns:
        raise InvalidSpecError(f"Unknown column: {request.target_column}")

    opts = request.plot_options or PlotOptions()
    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)

    series = df[request.target_column]
    title = f"Target distribution: {request.target_column}"
    if pd.api.types.is_numeric_dtype(series):
        sns.histplot(series.dropna(), bins=request.bins, kde=request.show_kde, ax=ax)
        y_label = "Frequency"
    else:
        counts = series.value_counts()
        sns.barplot(x=counts.index.astype(str), y=counts.to_numpy(), ax=ax)
        y_label = "Count"

    finish_axes(ax, opts, title, request.target_column, y_label)
    fig.tight_layout()
    return fig, opts.title or title


def plot_correlation_heatmap(
    df: pd.DataFrame, request: CorrelationHeatmapRequest
) -> tuple[Figure, str]:
    """Plot a correlation heatmap, masking the upper triangle.

    Args:
        df: The dataset to plot.
        request: The request, naming the columns, correlation method and whether to
            annotate cells.

    Returns:
        The rendered figure and its title.

    Raises:
        InvalidSpecError: If a requested column doesn't exist or isn't numeric, or
            there are more than 50 columns to correlate.
    """
    columns = request.columns if request.columns is not None else numeric_columns(df)
    _check_numeric_columns(df, columns, _MAX_HEATMAP_COLUMNS)

    opts = request.plot_options or PlotOptions()
    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)

    corr = df[columns].corr(method=request.method)
    mask = np.triu(np.ones_like(corr, dtype=bool))
    annotate = request.annotate and len(columns) <= _MAX_ANNOTATED_HEATMAP_COLUMNS
    sns.heatmap(
        corr,
        mask=mask,
        cmap=opts.colour_map,
        vmin=-1.0,
        vmax=1.0,
        center=0,
        annot=annotate,
        fmt=".2f",
        square=True,
        ax=ax,
    )

    title = "Correlation heatmap"
    finish_axes(ax, opts, title, None, None)
    fig.tight_layout()
    return fig, opts.title or title


def create_pairplot(df: pd.DataFrame, request: PairplotRequest) -> tuple[Figure, str]:
    """Plot a pairplot of the requested numeric columns.

    Samples 2000 rows (``random_state=42``) when the dataset has more, noting it in
    the title.

    Args:
        df: The dataset to plot.
        request: The request, naming the columns, optional hue column, and pairplot
            style.

    Returns:
        The rendered figure and its title.

    Raises:
        InvalidSpecError: If a requested column (including ``hue_column``) doesn't
            exist, or a plotted column isn't numeric.
    """
    _check_numeric_columns(df, request.columns)
    if request.hue_column is not None and request.hue_column not in df.columns:
        raise InvalidSpecError(f"Unknown column: {request.hue_column}")

    opts = request.plot_options or PlotOptions()
    apply_style(opts)

    columns = list(request.columns)
    if request.hue_column is not None and request.hue_column not in columns:
        columns = [*columns, request.hue_column]
    data = df[columns]

    sampled = len(data) > _PAIRPLOT_SAMPLE_ROWS
    if sampled:
        data = data.sample(
            n=_PAIRPLOT_SAMPLE_ROWS, random_state=_PAIRPLOT_SAMPLE_RANDOM_STATE
        )

    grid = sns.pairplot(
        data,
        vars=request.columns,
        hue=request.hue_column,
        corner=request.corner,
        diag_kind=request.diag_kind,
    )

    title = "Pairplot"
    if sampled:
        title = f"{title} (sampled {_PAIRPLOT_SAMPLE_ROWS} of {len(df)} rows)"
    grid.figure.suptitle(opts.title or title, fontsize=opts.title_font_size, y=1.02)
    grid.figure.set_dpi(opts.dpi)
    return grid.figure, opts.title or title


def _check_numeric_columns(
    df: pd.DataFrame, columns: list[str], max_columns: int | None = None
) -> None:
    """Validate that every column exists, is numeric, and the count is within limits.

    Args:
        df: The dataset the columns are drawn from.
        columns: The column names to validate.
        max_columns: The maximum number of columns allowed, or ``None`` to skip that
            check (when the request model already bounds the count).

    Raises:
        InvalidSpecError: If a column is unknown or non-numeric, or there are more
            than ``max_columns`` of them.
    """
    if max_columns is not None and len(columns) > max_columns:
        raise InvalidSpecError(f"Too many columns: at most {max_columns} allowed")
    unknown = [c for c in columns if c not in df.columns]
    if unknown:
        raise InvalidSpecError(f"Unknown column(s): {', '.join(unknown)}")
    non_numeric = [c for c in columns if not pd.api.types.is_numeric_dtype(df[c])]
    if non_numeric:
        raise InvalidSpecError(f"Non-numeric column(s): {', '.join(non_numeric)}")
