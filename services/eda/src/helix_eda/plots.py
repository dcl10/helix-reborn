"""Pure plotting functions for the EDA service's plot endpoints.

Ported from the original Helix's ``helix/services/plotting.py``, dropping all
Streamlit code. Each function takes the loaded dataset and its request model and
returns a ``(Figure, title)`` pair; routing, validation and upload to S3 live in
:mod:`helix_eda.routes_plots`.
"""

import numpy as np
import pandas as pd
import seaborn as sns
from matplotlib.axes import Axes
from matplotlib.figure import Figure
from sklearn.decomposition import PCA
from sklearn.manifold import TSNE
from sklearn.preprocessing import MinMaxScaler, StandardScaler

from helix_core.datasets import numeric_columns
from helix_core.errors import InvalidSpecError
from helix_core.messages import PlotOptions
from helix_core.plotting import apply_style, finish_axes, use_headless_backend
from helix_eda.schemas import (
    CorrelationHeatmapRequest,
    DimensionalityReductionRequest,
    PairplotRequest,
    PcaRequest,
    TargetDistributionRequest,
    TsneRequest,
)

use_headless_backend()

_MAX_HEATMAP_COLUMNS = 50
_MAX_ANNOTATED_HEATMAP_COLUMNS = 20
_PAIRPLOT_SAMPLE_ROWS = 2000
_PAIRPLOT_SAMPLE_RANDOM_STATE = 42
_TSNE_MAX_ROWS = 5000
_TSNE_RANDOM_STATE = 42
_PCA_RANDOM_STATE = 42
_SCALERS = {"standard": StandardScaler, "minmax": MinMaxScaler}


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


def pca_plot(df: pd.DataFrame, request: PcaRequest) -> tuple[Figure, str]:
    """Plot the first two principal components of the requested numeric columns.

    Rows with a NaN in ``columns`` (or in ``colour_column``, if numeric and used for
    colouring) are dropped, the remaining data is scaled per ``request.scaling``, then
    reduced with ``PCA(n_components=2, random_state=42)``.

    Args:
        df: The dataset to plot.
        request: The request, naming the columns, optional colour column and scaling.

    Returns:
        The rendered figure and its title.

    Raises:
        InvalidSpecError: If a requested column (including ``colour_column``) doesn't
            exist, a plotted column isn't numeric, or fewer than 2 columns are given.
    """
    data, colours = _prepare_dimensionality_reduction_data(df, request)
    scaled = _scale(data, request.scaling)

    pca = PCA(n_components=2, random_state=_PCA_RANDOM_STATE)
    embedded = pca.fit_transform(scaled)
    explained = pca.explained_variance_ratio_ * 100

    opts = request.plot_options or PlotOptions()
    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)
    _scatter(ax, embedded, colours, opts)

    title = "PCA"
    finish_axes(
        ax,
        opts,
        title,
        f"PC1 ({explained[0]:.1f}%)",
        f"PC2 ({explained[1]:.1f}%)",
    )
    fig.tight_layout()
    return fig, opts.title or title


def tsne_plot(df: pd.DataFrame, request: TsneRequest) -> tuple[Figure, str]:
    """Plot a 2D t-SNE embedding of the requested numeric columns.

    Rows with a NaN in ``columns`` (or in ``colour_column``, if numeric and used for
    colouring) are dropped, then the data is sampled to at most 5000 rows
    (``random_state=42``), scaled per ``request.scaling``, and reduced with
    ``TSNE(n_components=2, perplexity=…, random_state=42, init="pca")``.

    Args:
        df: The dataset to plot.
        request: The request, naming the columns, optional colour column, scaling and
            perplexity.

    Returns:
        The rendered figure and its title.

    Raises:
        InvalidSpecError: If a requested column (including ``colour_column``) doesn't
            exist, a plotted column isn't numeric, fewer than 2 columns are given, or
            ``perplexity`` is not less than the number of rows (after sampling).
    """
    data, colours = _prepare_dimensionality_reduction_data(df, request)

    sampled = len(data) > _TSNE_MAX_ROWS
    if sampled:
        data = data.sample(n=_TSNE_MAX_ROWS, random_state=_TSNE_RANDOM_STATE)
        colours = colours.loc[data.index] if colours is not None else None

    if request.perplexity >= len(data):
        raise InvalidSpecError(
            f"perplexity ({request.perplexity}) must be less than the number of "
            f"rows ({len(data)})"
        )

    scaled = _scale(data, request.scaling)
    tsne = TSNE(
        n_components=2,
        perplexity=request.perplexity,
        random_state=_TSNE_RANDOM_STATE,
        init="pca",
    )
    embedded = tsne.fit_transform(scaled)

    opts = request.plot_options or PlotOptions()
    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)
    _scatter(ax, embedded, colours, opts)

    title = "t-SNE"
    if sampled:
        title = f"{title} (sampled {_TSNE_MAX_ROWS} of {len(df)} rows)"
    finish_axes(ax, opts, title, "t-SNE 1", "t-SNE 2")
    fig.tight_layout()
    return fig, opts.title or title


def _prepare_dimensionality_reduction_data(
    df: pd.DataFrame, request: DimensionalityReductionRequest
) -> tuple[pd.DataFrame, pd.Series | None]:
    """Validate and select the data for a PCA/t-SNE plot.

    Args:
        df: The dataset to plot.
        request: The request, naming the columns and optional colour column.

    Returns:
        A tuple of the columns' data (NaN rows dropped) and the colour column's data
        aligned to it (or ``None``).

    Raises:
        InvalidSpecError: If ``colour_column`` doesn't exist, a plotted column isn't
            numeric, or fewer than 2 columns are given.
    """
    columns = request.columns if request.columns is not None else numeric_columns(df)
    if len(columns) < 2:
        raise InvalidSpecError("At least 2 columns are required")
    _check_numeric_columns(df, columns)
    if request.colour_column is not None and request.colour_column not in df.columns:
        raise InvalidSpecError(f"Unknown column: {request.colour_column}")

    subset_columns = list(columns)
    has_colour = (
        request.colour_column is not None and request.colour_column not in columns
    )
    if has_colour:
        subset_columns = [*subset_columns, request.colour_column]
    subset = df[subset_columns].dropna()

    data = subset[columns]
    colours = (
        subset[request.colour_column] if request.colour_column is not None else None
    )
    return data, colours


def _scale(data: pd.DataFrame, scaling: str) -> np.ndarray:
    """Scale a dataframe's values per the requested scaling method.

    Args:
        data: The data to scale.
        scaling: One of ``"standard"``, ``"minmax"`` or ``"none"``.

    Returns:
        The scaled values as a NumPy array (unchanged if ``scaling == "none"``).
    """
    if scaling == "none":
        return data.to_numpy()
    return _SCALERS[scaling]().fit_transform(data)


def _scatter(
    ax: Axes, embedded: np.ndarray, colours: pd.Series | None, opts: PlotOptions
) -> None:
    """Draw a 2D scatter plot, coloured continuously or categorically.

    Args:
        ax: The axes to draw on.
        embedded: The 2D coordinates to plot, shape ``(n, 2)``.
        colours: The values to colour points by, or ``None`` for a single colour.
        opts: Styling options, for the colour map.
    """
    frame = pd.DataFrame(embedded, columns=["x", "y"])
    if colours is not None:
        frame["colour"] = colours.to_numpy()
        if pd.api.types.is_numeric_dtype(colours):
            points = ax.scatter(
                frame["x"], frame["y"], c=frame["colour"], cmap=opts.colour_map
            )
            ax.figure.colorbar(points, ax=ax, label=str(colours.name))
        else:
            sns.scatterplot(data=frame, x="x", y="y", hue="colour", ax=ax)
            ax.legend(title=str(colours.name))
    else:
        ax.scatter(frame["x"], frame["y"])


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
