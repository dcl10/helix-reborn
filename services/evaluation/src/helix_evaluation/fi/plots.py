"""Plots for global and local feature importance.

Ported from the original Helix's ``helix/services/plotting.py``
(``plot_permutation_importance``, ``plot_global_shap_importance``,
``plot_most_important_feats_violin``), restyled with
``helix_core.plotting``'s ``apply_style``/``finish_axes`` instead of raw
``plt.style.use``/``ax.set_*`` calls, to match the rest of this repo
(:mod:`helix_eda.plots`).
"""

import pandas as pd
import seaborn as sns
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


def plot_local_importance_violin(
    df: pd.DataFrame, opts: PlotOptions, num_features_to_plot: int, title: str
) -> Figure:
    """Plot the distribution of local importances for the top features as violins.

    Args:
        df: Local importances: one row per explained sample, one column per
            feature, as returned by
            :func:`~helix_evaluation.fi.local_methods.lime_values` or
            :func:`~helix_evaluation.fi.local_methods.local_shap_values`.
        opts: Shared plot styling options.
        num_features_to_plot: The number of top features (by mean absolute value)
            to plot.
        title: The plot's title.

    Returns:
        The rendered figure, with features ordered by mean absolute importance,
        most important first.
    """
    top_features = (
        df.abs().mean().sort_values(ascending=False).head(num_features_to_plot).index
    )

    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)
    long = df.loc[:, top_features].melt(var_name="feature", value_name="importance")
    sns.violinplot(
        data=long,
        x="feature",
        y="importance",
        hue="feature",
        order=list(top_features),
        fill=True,
        ax=ax,
        palette=opts.colour_map,
        legend=False,
    )
    finish_axes(ax, opts, title, "Feature", "Importance")
    fig.tight_layout()
    return fig


def plot_shap_waterfall(
    explanation: shap.Explanation,
    opts: PlotOptions,
    num_features_to_plot: int,
    title: str,
) -> Figure:
    """Plot a SHAP waterfall plot for one explained sample.

    Like :func:`plot_shap_beeswarm`, SHAP draws on the current figure, so it is
    read back with ``plt.gcf()``.

    Args:
        explanation: The SHAP explanation of a single sample (for example
            ``explanation[0]``).
        opts: Shared plot styling options.
        num_features_to_plot: The number of features to show before the rest are
            collapsed into one bar.
        title: The plot's title.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    plt.figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    shap.plots.waterfall(explanation, max_display=num_features_to_plot, show=False)
    fig = plt.gcf()
    fig.axes[0].set_title(opts.title or title, fontsize=opts.title_font_size)
    fig.tight_layout()
    return fig
