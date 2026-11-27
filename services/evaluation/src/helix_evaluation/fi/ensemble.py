"""Ensemble feature importance: combining global methods by mean and majority vote.

Ported from the original Helix's
``helix/services/feature_importance/ensemble_methods.py``
(``calculate_ensemble_mean``, ``calculate_ensemble_majorityvote``) with the logic
unchanged, and ``FeatureImportanceEstimator._ensemble_feature_importance`` as
:class:`EnsembleStep`. The ``Logger`` argument is replaced by
``logging.getLogger(__name__)``.

An evaluation covers one model, so the input dict has a single key (the run's
model) mapped to ``ctx.results["global_importance"]``: rows are features, columns
are methods, values are min-max normalised to ``[0, 1]``.
"""

import logging

import pandas as pd

from helix_core.errors import InvalidSpecError
from helix_core.messages import EvaluationOptions
from helix_evaluation.context import EvaluationContext
from helix_evaluation.fi.plots import plot_ensemble_importance

logger = logging.getLogger(__name__)

MEAN_COLUMN = "Mean Importance"
MAJORITY_VOTE_COLUMN = "Majority Vote Importance"


def ensemble_mean(fi: dict[str, pd.DataFrame]) -> tuple[pd.DataFrame, pd.DataFrame]:
    """Average normalised importances across methods, then across models.

    Args:
        fi: Model name to a dataframe of rows = features, columns = methods,
            values normalised to ``[0, 1]``.

    Returns:
        A tuple of the mean importance per feature (column ``"Mean Importance"``,
        sorted descending) and its standard deviation across repeated rows for
        the same feature, in the same order. With one model and no repeated
        features the standard deviation is all ``NaN``.
    """
    logger.info("Calculating Mean ensemble importance")

    model_means = {
        model_name: importance_df.mean(axis=1)
        for model_name, importance_df in fi.items()
    }
    all_model_means = pd.DataFrame(model_means)
    combined_mean = all_model_means.mean(axis=1).to_frame(name=MEAN_COLUMN)

    grouped = combined_mean.groupby(level=0)
    mean_df = grouped.mean()
    std_df = grouped.std()

    mean_df = mean_df.sort_values(MEAN_COLUMN, ascending=False)
    std_df = std_df.reindex(mean_df.index)
    return mean_df, std_df


def ensemble_majority_vote(
    fi: dict[str, pd.DataFrame],
) -> tuple[pd.DataFrame, pd.DataFrame]:
    """Average each feature's importances over the methods that agree on its rank.

    Each method ranks the features (dense, highest importance = 1). For each
    feature, the most common rank across methods is found; the feature's
    importance is the mean of its values, with the values from methods that gave
    it a different rank counted as 0. When ranks tie for most common, the first
    two are kept (as in the original Helix); a third or later tied rank is
    dropped.

    Args:
        fi: Model name to a dataframe of rows = features, columns = methods,
            values normalised to ``[0, 1]``.

    Returns:
        A tuple of the majority-vote importance per feature (column
        ``"Majority Vote Importance"``, sorted descending) and its standard
        deviation across repeated rows for the same feature, in the same order.
        An empty input gives two empty frames with that column.
    """
    logger.info("Calculating Majority Vote ensemble importance")

    combined = pd.concat(fi.values())
    if combined.empty:
        empty = pd.DataFrame(columns=[MAJORITY_VOTE_COLUMN])
        return empty, empty.copy()

    ranks = combined.rank(axis=0, method="dense", ascending=False)
    mode_ranks = ranks.mode(axis=1)

    at_majority_rank = ranks.eq(mode_ranks[0], axis=0)
    if len(mode_ranks.columns) > 1:
        at_majority_rank = at_majority_rank | ranks.eq(mode_ranks[1], axis=0)

    votes = (
        combined.where(at_majority_rank, 0)
        .mean(axis=1)
        .to_frame(name=MAJORITY_VOTE_COLUMN)
    )

    grouped = votes.groupby(level=0)
    vote_df = grouped.mean()
    std_df = grouped.std()

    vote_df = vote_df.sort_values(MAJORITY_VOTE_COLUMN, ascending=False)
    std_df = std_df.reindex(vote_df.index)
    return vote_df, std_df


class EnsembleStep:
    """Combines the global importance methods into ensemble importances."""

    name = "ensemble"
    weight = 0.05

    def enabled(self, options: EvaluationOptions) -> bool:
        """Return ``True`` when at least one ensemble method is requested.

        Args:
            options: The evaluation job's options.

        Returns:
            ``True`` if ``options.ensemble_methods`` is non-empty.
        """
        return bool(options.ensemble_methods)

    def run(self, ctx: EvaluationContext) -> None:
        """Compute the requested ensembles from ``ctx.results["global_importance"]``.

        Writes ``ensemble_mean`` and/or ``ensemble_majority_vote`` tables and bar
        charts, and sets ``ctx.results["ensemble_importance"]`` to
        ``{"Mean": df, "Majority Vote": df}``, including only the methods that
        ran (the keys the fuzzy step expects).

        Args:
            ctx: The evaluation context to read global importance from and write
                tables, plots and the ``ensemble_importance`` result into.

        Raises:
            InvalidSpecError: If no global importance method ran.
        """
        global_importance = ctx.results.get("global_importance")
        if global_importance is None or global_importance.empty:
            raise InvalidSpecError(
                "Ensemble importance needs at least one global method"
            )

        options = ctx.job.options
        fi = {ctx.metadata.pipeline.model.step: global_importance}
        results: dict[str, pd.DataFrame] = {}

        if "mean" in options.ensemble_methods:
            mean_df, std_df = ensemble_mean(fi)
            self._save(ctx, "ensemble_mean", "mean", mean_df, std_df, MEAN_COLUMN)
            results["Mean"] = mean_df

        if "majority_vote" in options.ensemble_methods:
            vote_df, std_df = ensemble_majority_vote(fi)
            self._save(
                ctx,
                "ensemble_majority_vote",
                "majority vote",
                vote_df,
                std_df,
                MAJORITY_VOTE_COLUMN,
            )
            results["Majority Vote"] = vote_df

        ctx.results["ensemble_importance"] = results

    @staticmethod
    def _save(
        ctx: EvaluationContext,
        name: str,
        label: str,
        df: pd.DataFrame,
        std_df: pd.DataFrame,
        column: str,
    ) -> None:
        """Save one ensemble's table and bar chart.

        Args:
            ctx: The evaluation context to save into.
            name: The table and plot name.
            label: The method's name in the title (``"mean"``, ``"majority vote"``).
            df: The ensemble importances.
            std_df: Their standard deviations, used as error bars.
            column: The importance column of ``df`` and ``std_df``.
        """
        title = f"Ensemble importance: {label}"
        ctx.save_table(name, df, title)
        ctx.save_plot(
            name,
            plot_ensemble_importance(
                df,
                std_df,
                column,
                ctx.plot_options,
                ctx.job.options.num_features_to_plot,
                title,
            ),
            title,
        )
