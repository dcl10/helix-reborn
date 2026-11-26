"""Request and response models for the Helix EDA service."""

from typing import Literal

from pydantic import BaseModel, Field, field_validator

from helix_core.messages import PlotOptions


class SummaryRequest(BaseModel):
    """Request body for ``POST /summary``."""

    dataset_key: str = Field(description="S3 key of the CSV dataset to summarise.")


class PlotRequest(BaseModel):
    """Fields shared by every plot endpoint's request body."""

    dataset_key: str = Field(description="S3 key of the CSV dataset to plot.")
    output_key: str = Field(description="S3 key to write the PNG to. Must end '.png'.")
    plot_options: PlotOptions | None = Field(
        None, description="Styling options. Defaults are used for any field omitted."
    )

    @field_validator("output_key")
    @classmethod
    def _check_output_key_ends_with_png(cls, value: str) -> str:
        """Require ``output_key`` to end with ``.png``.

        Args:
            value: The ``output_key`` field value.

        Returns:
            The value, unchanged.

        Raises:
            ValueError: If ``value`` doesn't end with ``.png``.
        """
        if not value.endswith(".png"):
            raise ValueError("output_key must end with '.png'")
        return value


class TargetDistributionRequest(PlotRequest):
    """Request body for ``POST /plots/target-distribution``."""

    target_column: str = Field(description="Column to plot the distribution of.")
    show_kde: bool = Field(True, description="Overlay a KDE curve for numeric targets.")
    bins: int = Field(30, ge=5, le=200, description="Histogram bin count.")


class CorrelationHeatmapRequest(PlotRequest):
    """Request body for ``POST /plots/correlation-heatmap``."""

    columns: list[str] | None = Field(
        None, description="Columns to correlate. Defaults to all numeric columns."
    )
    method: Literal["pearson", "spearman", "kendall"] = Field(
        "pearson", description="Correlation method."
    )
    annotate: bool = Field(True, description="Show coefficients, if 20 columns or fewer.")


class PairplotRequest(PlotRequest):
    """Request body for ``POST /plots/pairplot``."""

    columns: list[str] = Field(
        min_length=2, max_length=10, description="Numeric columns to pair up."
    )
    hue_column: str | None = Field(None, description="Column to colour points by.")
    corner: bool = Field(True, description="Only plot the lower triangle.")
    diag_kind: Literal["hist", "kde"] = Field("hist", description="Diagonal plot kind.")


class PlotResponse(BaseModel):
    """Response returned by every plot endpoint."""

    key: str = Field(description="The S3 key the PNG was written to.")
    content_type: Literal["image/png"] = "image/png"
    size_bytes: int = Field(description="Size of the uploaded PNG, in bytes.")
    title: str = Field(description="The plot's title, as rendered.")


class TopValue(BaseModel):
    """One value and its occurrence count, for a non-numeric column."""

    value: str = Field(description="The value, rendered as a string.")
    count: int = Field(description="How many rows hold this value.")


class ColumnSummary(BaseModel):
    """Summary statistics for one column of a dataset."""

    name: str = Field(description="The column's name.")
    dtype: str = Field(description="The pandas dtype, as a string (e.g. 'float64').")
    is_numeric: bool = Field(description="Whether the column holds numeric values.")
    non_null: int = Field(description="Number of non-null values.")
    missing: int = Field(description="Number of missing (null) values.")
    missing_pct: float = Field(description="Percentage of rows missing, 0-100.")
    unique: int = Field(description="Number of distinct non-null values.")
    mean: float | None = Field(description="Mean, or null for non-numeric columns.")
    std: float | None = Field(
        description="Standard deviation, or null for non-numeric columns."
    )
    min: float | None = Field(description="Minimum, or null for non-numeric columns.")
    q25: float | None = Field(
        description="25th percentile, or null for non-numeric columns."
    )
    median: float | None = Field(description="Median, or null for non-numeric columns.")
    q75: float | None = Field(
        description="75th percentile, or null for non-numeric columns."
    )
    max: float | None = Field(description="Maximum, or null for non-numeric columns.")
    top_values: list[TopValue] | None = Field(
        description="Up to 10 most common values, or null for numeric columns."
    )


class DatasetSummary(BaseModel):
    """Summary of a tabular dataset, returned by ``POST /summary``."""

    row_count: int = Field(description="Number of rows in the dataset.")
    column_count: int = Field(description="Number of columns in the dataset.")
    columns: list[ColumnSummary] = Field(
        description="Per-column summaries, in the dataset's column order."
    )
