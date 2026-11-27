"""Fuzzy interpretation: human-readable rules from local feature importance.

Ported from the original Helix's ``helix/feature_importance/fuzzy.py`` (class
``Fuzzy``) and the plotting helpers ``save_fuzzy_sets_plots`` and
``save_target_clusters_plots`` from
``helix/services/feature_importance/results.py``. The membership functions,
thresholds, clustering and rule extraction are the original's. Differences, each
needed for the step to do what its options say:

- Top-feature selection sorts by the ensemble frame's importance column (the
  original sorted by a column named ``0``, which doesn't exist, so selection
  always failed), and the rules use only the selected features (the original
  selected features but built rules from all of them).
- Local importances are min-max normalised per method here, as the original's
  ``FeatureImportanceEstimator._stack_local_importances`` did before calling
  ``Fuzzy``. The target is appended unnormalised (class labels for
  classification), so contexts read as values or class names rather than numbers
  in ``[0, 1]``.
- Membership-function plots are drawn once, from the training features (the
  original drew train then test plots to the same file names).
- Fuzzy c-means is seeded, so results are reproducible.
- The rules table is the top ``number_rules`` rules per context with how many
  samples follow each (the original wrote the per-sample rules, then overwrote
  that file with the synergy summary).

How to read the outputs: each explained sample's local importance for a feature is
labelled ``low``, ``medium`` or ``high`` relative to the other samples. Samples
are grouped by *context*: the target's class for classification, or a fuzzy
cluster of the target (named by ``cluster_names``, lowest first) for regression.
The most common label patterns in each context are the rules; the summary picks,
per context and feature, the highest label that appears in any top rule.
"""

import logging
import re
import warnings
from dataclasses import dataclass, field

import numpy as np
import pandas as pd
import seaborn as sns
import skfuzzy as fuzz
from matplotlib.figure import Figure

from helix_core.messages import (
    EvaluationOptions,
    FuzzyOptions,
    PlotOptions,
    ProblemType,
)
from helix_core.plotting import apply_style, finish_axes
from helix_evaluation.context import EvaluationContext
from helix_evaluation.fi.global_methods import normalise

logger = logging.getLogger(__name__)

_FUZZY_SETS = ("low", "medium", "high")
_CMEANS_FUZZINESS = 2
_CMEANS_ERROR = 0.005
_CMEANS_MAX_ITER = 1000
_RECORDS = "records"

MembershipFunctions = dict[str, dict[str, np.ndarray]]


@dataclass
class FuzzyResult:
    """The outputs of :meth:`FuzzyInterpreter.interpret`.

    Attributes:
        fuzzy_rules: The top ``number_rules`` rules per context: one column per
            selected feature (``low``/``medium``/``high``), the context column,
            and ``records``, the number of samples following the rule.
        contextual_rules: Rows = features, columns = contexts; each value is the
            highest fuzzy set (``high`` > ``medium`` > ``low``) that feature has
            in any of that context's top rules.
        figures: Plot name to ``(figure, title)``: the membership functions per
            feature (``fuzzy_sets_{feature}``) when ``granular_features`` is set,
            and ``fuzzy_target_clusters`` for regression.
    """

    fuzzy_rules: pd.DataFrame
    contextual_rules: pd.DataFrame
    figures: dict[str, tuple[Figure, str]] = field(default_factory=dict)


def sanitise_name(name: str) -> str:
    """Make a feature name safe for an artifact file name.

    Args:
        name: The feature name.

    Returns:
        ``name`` with every character outside ``[A-Za-z0-9_-]`` replaced by ``_``.
    """
    return re.sub(r"[^A-Za-z0-9_-]", "_", name)


def plot_fuzzy_sets(
    feature: str,
    universe: np.ndarray,
    membership_functions: dict[str, np.ndarray],
    opts: PlotOptions,
) -> Figure:
    """Plot one feature's low/medium/high membership functions.

    Port of the original Helix's ``save_fuzzy_sets_plots`` for a single feature.

    Args:
        feature: The feature's name, used in the title.
        universe: The feature's value range the functions are defined over.
        membership_functions: ``{"low": …, "medium": …, "high": …}`` membership
            degrees over ``universe``.
        opts: Shared plot styling options.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)
    for fuzzy_set, colour in zip(_FUZZY_SETS, ("r", "g", "b"), strict=True):
        ax.plot(universe, membership_functions[fuzzy_set], colour, label=fuzzy_set)
    finish_axes(ax, opts, f"{feature} membership functions", feature, "Membership")
    ax.legend()
    fig.tight_layout()
    return fig


def plot_target_clusters(df_cluster: pd.DataFrame, opts: PlotOptions) -> Figure:
    """Plot the target's distribution within each fuzzy cluster as box plots.

    Port of the original Helix's ``save_target_clusters_plots``.

    Args:
        df_cluster: A dataframe with columns ``target`` (values) and ``cluster``
            (cluster names).
        opts: Shared plot styling options.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    fig = Figure(figsize=(opts.width, opts.height), dpi=opts.dpi)
    ax = fig.add_subplot(111)
    sns.boxplot(
        data=df_cluster,
        x="cluster",
        y="target",
        hue="cluster",
        palette=opts.colour_map,
        legend=False,
        ax=ax,
    )
    finish_axes(ax, opts, "Target clusters", "Cluster", "Target")
    fig.tight_layout()
    return fig


class FuzzyInterpreter:
    """Interprets the synergy of feature importances by context.

    Port of the original Helix's ``Fuzzy`` class.
    """

    def __init__(
        self,
        options: FuzzyOptions,
        problem_type: ProblemType,
        plot_options: PlotOptions,
        random_state: int = 0,
    ) -> None:
        """Create an interpreter.

        Args:
            options: The fuzzy options.
            problem_type: ``"classification"`` or ``"regression"``.
            plot_options: Shared plot styling options.
            random_state: The seed for fuzzy c-means clustering of the target.
        """
        self._options = options
        self._problem_type = problem_type
        self._plot_options = plot_options
        self._random_state = random_state

    def interpret(
        self,
        ensemble_results: dict[str, pd.DataFrame],
        local_results: dict[str, pd.DataFrame],
        x_train: pd.DataFrame,
        target: pd.Series,
    ) -> FuzzyResult:
        """Extract fuzzy rules from local importances, by context.

        Args:
            ensemble_results: ``{"Mean": df, "Majority Vote": df}`` (either or
                both), each a single importance column indexed by feature.
            local_results: Local importances per method (``{"LIME": df, "SHAP":
                df}``), rows = explained samples, columns = features.
            x_train: The training features, used for the membership-function
                plots.
            target: The target value of each explained sample, indexed like the
                ``local_results`` frames and named after the target column.
                Class labels for classification, values for regression.

        Returns:
            The rules, the per-context summary and the figures.
        """
        figures: dict[str, tuple[Figure, str]] = {}

        features = self._select_features(ensemble_results)
        if features is None:
            features = list(x_train.columns)

        if self._options.granular_features:
            figures.update(self._make_fuzzy_set_mfs(x_train[features]))

        local_importance_df = pd.concat(
            [
                self._normalise(df[features]).assign(
                    **{str(target.name): target.loc[df.index].to_numpy()}
                )
                for df in local_results.values()
            ],
            axis=0,
            ignore_index=True,
        )

        fuzzy_rules_df, cluster_figure = self._make_user_granularity_fuzzy_sets(
            local_importance_df
        )
        if cluster_figure is not None:
            figures["fuzzy_target_clusters"] = (cluster_figure, "Target clusters")

        top_rules, contextual_rules = self._contextual_synergy_analysis(fuzzy_rules_df)
        return FuzzyResult(
            fuzzy_rules=top_rules,
            contextual_rules=contextual_rules,
            figures=figures,
        )

    def _select_features(
        self, ensemble_results: dict[str, pd.DataFrame]
    ) -> list[str] | None:
        """Pick the top features by majority vote, falling back to the mean.

        Args:
            ensemble_results: The ensemble importances by method name.

        Returns:
            The top ``number_fuzzy_features`` features, or ``None`` if neither
            ensemble could be used.
        """
        for method in ("Majority Vote", "Mean"):
            try:
                importance = ensemble_results[method]
                ranked = importance.sort_values(
                    by=importance.columns[0], ascending=False
                )
                return ranked.index[: self._options.number_fuzzy_features].tolist()
            except (KeyError, IndexError) as error:
                logger.warning(
                    "Fuzzy feature selection from %r failed (%r); trying the next "
                    "ensemble method",
                    method,
                    error,
                )
        logger.warning("Fuzzy feature selection failed; using all features")
        return None

    @staticmethod
    def _normalise(df: pd.DataFrame) -> pd.DataFrame:
        """Min-max scale each column to ``[0, 1]`` (a constant column becomes 0).

        Args:
            df: Local importances of one method.

        Returns:
            The scaled importances.
        """
        return df.apply(normalise)

    def _make_fuzzy_set_mfs(self, x: pd.DataFrame) -> dict[str, tuple[Figure, str]]:
        """Build each feature's low/medium/high membership functions and plot them.

        Low is a Z function, medium a triangle and high an S function, with
        breakpoints at the feature's quartiles. Features with fewer than three
        unique values get spikes at the minimum, midpoint and maximum; features
        whose minimum equals their median get triangles from the median upwards.

        Args:
            x: The features to build membership functions for.

        Returns:
            Plot name to ``(figure, title)`` for each feature.
        """
        logger.info("Creating fuzzy set membership functions for features")
        quantiles = x.quantile([0, 0.25, 0.5, 0.75, 1])
        figures: dict[str, tuple[Figure, str]] = {}

        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            for feature in x.columns:
                q = quantiles[feature]
                universe = np.linspace(x[feature].min(), x[feature].max(), 100)

                if len(x[feature].unique()) < 3:
                    middle = (q[0.00] + q[1.00]) / 2
                    low_mf = fuzz.trimf(universe, [q[0.00], q[0.00], q[0.00]])
                    medium_mf = fuzz.trimf(universe, [middle, middle, middle])
                    high_mf = fuzz.trimf(universe, [q[1.00], q[1.00], q[1.00]])
                elif q[0.00] == q[0.50]:
                    low_mf = fuzz.trimf(universe, [q[0.00], q[0.50], q[0.75]])
                    medium_mf = fuzz.trimf(universe, [q[0.50], q[0.75], q[1.00]])
                    high_mf = fuzz.smf(universe, q[0.75], q[1.00])
                else:
                    low_mf = fuzz.zmf(universe, q[0.00], q[0.50])
                    medium_mf = fuzz.trimf(universe, [q[0.25], q[0.50], q[0.75]])
                    high_mf = fuzz.smf(universe, q[0.50], q[1.00])

                title = f"{feature} membership functions"
                figures[f"fuzzy_sets_{sanitise_name(str(feature))}"] = (
                    plot_fuzzy_sets(
                        str(feature),
                        universe,
                        {"low": low_mf, "medium": medium_mf, "high": high_mf},
                        self._plot_options,
                    ),
                    title,
                )
        return figures

    @staticmethod
    def _fuzzyset_selection(
        universe: np.ndarray,
        low_mf: np.ndarray,
        medium_mf: np.ndarray,
        high_mf: np.ndarray,
        value: float,
    ) -> str:
        """Pick the fuzzy set a value belongs to most (the first on ties).

        Args:
            universe: The value range the functions are defined over.
            low_mf: The low membership function.
            medium_mf: The medium membership function.
            high_mf: The high membership function.
            value: The value to classify.

        Returns:
            ``"low"``, ``"medium"`` or ``"high"``.
        """
        mf_values = [
            fuzz.interp_membership(universe, mf, value)
            for mf in (low_mf, medium_mf, high_mf)
        ]
        return _FUZZY_SETS[mf_values.index(max(mf_values))]

    def _make_user_granularity_fuzzy_sets(
        self, df: pd.DataFrame
    ) -> tuple[pd.DataFrame, Figure | None]:
        """Label each sample's importances low/medium/high, by context.

        For regression the target (the last column) is first clustered with fuzzy
        c-means into ``number_clusters`` clusters, named by ``cluster_names`` in
        order of their average target value.

        Args:
            df: Normalised local importances, one column per feature, with the
                target as the last column.

        Returns:
            The per-sample rules (features labelled ``low``/``medium``/``high``,
            then the context column) and, for regression, the target-clusters
            figure.
        """
        df = df.copy()
        target_column = df.columns[-1]
        cluster_figure = None

        if self._problem_type == "regression":
            target = np.array(df[target_column], dtype=np.float64)
            _, membership_matrix, *_ = fuzz.cluster.cmeans(
                data=target.reshape(1, -1),
                c=self._options.number_clusters,
                m=_CMEANS_FUZZINESS,
                error=_CMEANS_ERROR,
                maxiter=_CMEANS_MAX_ITER,
                seed=self._random_state,
            )
            primary_cluster_assignment = np.argmax(membership_matrix, axis=0)

            cluster_numbers = np.unique(primary_cluster_assignment).tolist()
            with warnings.catch_warnings():
                warnings.simplefilter("ignore", RuntimeWarning)
                cluster_averages = [
                    np.mean(target[primary_cluster_assignment == i])
                    for i in range(self._options.number_clusters)
                ]

            clusters = list(zip(cluster_numbers, cluster_averages, strict=False))
            clusters.sort(key=lambda cluster: cluster[1])
            cluster_mapping = {
                cluster[0]: name
                for cluster, name in zip(
                    clusters, self._options.cluster_names, strict=False
                )
            }
            assignment = [cluster_mapping[c] for c in primary_cluster_assignment]

            cluster_figure = plot_target_clusters(
                pd.DataFrame({"target": target, "cluster": assignment}),
                self._plot_options,
            )
            df[target_column] = assignment

        logger.info("Creating fuzzy set membership functions for importances")
        membership_functions: MembershipFunctions = {}
        universe: dict[str, np.ndarray] = {}
        for feature in df.columns[:-1]:
            universe[feature] = np.linspace(df[feature].min(), df[feature].max(), 100)
            membership_functions[feature] = {
                "low": fuzz.zmf(universe[feature], 0.00, 0.5),
                "medium": fuzz.trimf(universe[feature], [0.25, 0.5, 0.75]),
                "high": fuzz.smf(universe[feature], 0.5, 1.00),
            }

        logger.info("Assigning samples to fuzzy sets")
        fuzzy_rules = []
        for i in df.index:
            fuzzy_sets: dict[str, object] = {
                feature: self._fuzzyset_selection(
                    universe[feature],
                    membership_functions[feature]["low"],
                    membership_functions[feature]["medium"],
                    membership_functions[feature]["high"],
                    df.loc[i, feature],
                )
                for feature in df.columns[:-1]
            }
            fuzzy_sets[target_column] = df.loc[i, target_column]
            fuzzy_rules.append(fuzzy_sets)

        return pd.DataFrame(fuzzy_rules, index=df.index), cluster_figure

    def _contextual_synergy_analysis(
        self, fuzzy_rules_df: pd.DataFrame
    ) -> tuple[pd.DataFrame, pd.DataFrame]:
        """Find the most common rules per context and summarise them.

        Args:
            fuzzy_rules_df: Per-sample rules, with the context as the last column.

        Returns:
            A tuple of the top ``number_rules`` rules per context (with a
            ``records`` count), and the per-context summary: rows = features,
            columns = contexts, values = the highest fuzzy set seen for that
            feature among the context's top rules.
        """
        fuzzy_rules_df = fuzzy_rules_df.dropna(how="all", axis=1)

        grouped = (
            fuzzy_rules_df.groupby(fuzzy_rules_df.columns.tolist())
            .size()
            .reset_index()
            .rename(columns={0: _RECORDS})
            .sort_values(by=[_RECORDS], ascending=False)
        )

        context_column = grouped.columns[-2]
        top_rules = {
            category: grouped[grouped[context_column] == category].head(
                self._options.number_rules
            )
            for category in grouped[context_column].unique()
        }

        synergy_features: dict[object, dict[str, str]] = {}
        for category, rules in top_rules.items():
            synergy_features[category] = {}
            for feature in rules.columns[:-2]:
                unique_values = rules[feature].unique()
                if "high" in unique_values:
                    top_value = "high"
                elif "medium" in unique_values:
                    top_value = "medium"
                else:
                    top_value = "low"
                synergy_features[category][feature] = top_value

        logger.info("Synergy and impact of features: %s", synergy_features)
        rules_df = pd.concat(top_rules.values(), ignore_index=True)
        return rules_df, pd.DataFrame(synergy_features)


class FuzzyStep:
    """Runs fuzzy interpretation on the local and ensemble importances."""

    name = "fuzzy"
    weight = 0.1

    def enabled(self, options: EvaluationOptions) -> bool:
        """Return ``True`` when fuzzy options are set.

        Args:
            options: The evaluation job's options.

        Returns:
            ``True`` if ``options.fuzzy`` is not ``None``.
        """
        return options.fuzzy is not None

    def run(self, ctx: EvaluationContext) -> None:
        """Interpret the earlier steps' results and save the rules and plots.

        Reads ``ctx.results["ensemble_importance"]`` and
        ``ctx.results["local_importance"]`` (which ``EvaluationOptions``
        validation guarantees were requested). Writes tables ``fuzzy_rules`` and
        ``fuzzy_contextual_rules``, plots ``fuzzy_sets_{feature}`` and, for
        regression, ``fuzzy_target_clusters``.

        Args:
            ctx: The evaluation context to read results from and save outputs to.
        """
        options = ctx.job.options.fuzzy
        assert options is not None

        local_results: dict[str, pd.DataFrame] = ctx.results["local_importance"]
        explained_index = next(iter(local_results.values())).index
        target = self._target(ctx).loc[explained_index]

        interpreter = FuzzyInterpreter(
            options,
            ctx.metadata.problem_type,
            ctx.plot_options,
            random_state=ctx.metadata.pipeline.split.random_seed,
        )
        result = interpreter.interpret(
            ctx.results["ensemble_importance"], local_results, ctx.x_train, target
        )

        ctx.save_table("fuzzy_rules", result.fuzzy_rules, "Fuzzy rules")
        ctx.save_table(
            "fuzzy_contextual_rules", result.contextual_rules, "Fuzzy rules by context"
        )
        for name, (fig, title) in result.figures.items():
            ctx.save_plot(name, fig, title)

    @staticmethod
    def _target(ctx: EvaluationContext) -> pd.Series:
        """Build the test set's target as a series named after the target column.

        Args:
            ctx: The evaluation context.

        Returns:
            The test targets indexed by test-set position: class labels for
            classification, values for regression.
        """
        values: np.ndarray | list[object] = ctx.y_test
        if ctx.metadata.problem_type == "classification":
            classes = ctx.metadata.classes or []
            values = [classes[index] for index in ctx.y_test]
        return pd.Series(
            values, index=ctx.x_test.index, name=ctx.metadata.target_column
        )
