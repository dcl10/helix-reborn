"""Plots for global feature importance.

Ported from the original Helix's ``helix/services/plotting.py``
(``plot_permutation_importance``, ``plot_global_shap_importance``), restyled with
``helix_core.plotting``'s ``apply_style``/``finish_axes`` instead of raw
``plt.style.use``/``ax.set_*`` calls, to match the rest of this repo
(:mod:`helix_eda.plots`).
"""

import pandas as pd
import shap
from matplotlib import pyplot as plt
from matplotlib.figure import Figure

from helix_core.messages import PlotOptions
from helix_core.plotting import apply_style, finish_axes


def plot_permutation_importance(
    df: pd.DataFrame, opts: PlotOptions, num_features_to_plot: int, title: str
) -> Figure:
    """Plot a horizontal bar chart of the top features by permutation importance.

    Args:
        df: A dataframe as returned by
            :func:`~helix_evaluation.fi.global_methods.permutation_importance_df`.
        opts: Shared plot styling options.
        num_features_to_plot: The number of top features to plot.
        title: The plot's title.

    Returns:
        The rendered figure, with the top feature at the top of the chart and
        standard-deviation error bars.
    """
    top = df.sort_values(by="importance_mean", ascending=False).head(
        num_features_to_plot
    )
    top = top.iloc[::-1]

    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)
    ax.barh(
        top.index,
        top["importance_mean"],
        xerr=top["importance_std"],
        capsize=5,
    )
    finish_axes(ax, opts, title, "Importance", "Feature")
    fig.tight_layout()
    return fig


def plot_global_shap_importance(
    df: pd.DataFrame, opts: PlotOptions, num_features_to_plot: int, title: str
) -> Figure:
    """Plot a horizontal bar chart of the top features by mean |SHAP value|.

    Args:
        df: A dataframe as returned by
            :func:`~helix_evaluation.fi.global_methods.shap_global_df`.
        opts: Shared plot styling options.
        num_features_to_plot: The number of top features to plot.
        title: The plot's title.

    Returns:
        The rendered figure, with the top feature at the top of the chart.
    """
    top = df.sort_values(by="mean_abs_shap", ascending=False).head(num_features_to_plot)
    top = top.iloc[::-1]

    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)
    ax.barh(top.index, top["mean_abs_shap"])
    finish_axes(ax, opts, title, "Mean |SHAP value|", "Feature")
    fig.tight_layout()
    return fig


def plot_shap_beeswarm(
    explanation: shap.Explanation,
    opts: PlotOptions,
    num_features_to_plot: int,
    title: str,
) -> Figure:
    """Plot a SHAP beeswarm plot of the top features.

    SHAP's beeswarm plotting draws on the current figure rather than taking one as
    an argument, so a figure is created first and ``plt.gcf()`` is read back after.

    Args:
        explanation: The SHAP explanation to plot.
        opts: Shared plot styling options.
        num_features_to_plot: The number of top features to plot.
        title: The plot's title.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    fig = plt.figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    shap.plots.beeswarm(explanation, max_display=num_features_to_plot, show=False)
    fig = plt.gcf()
    fig.axes[0].set_title(opts.title or title, fontsize=opts.title_font_size)
    fig.tight_layout()
    return fig
