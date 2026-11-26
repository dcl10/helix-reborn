"""Shared matplotlib/seaborn rendering helpers for the EDA and evaluation services.

Both read styling from :class:`helix_core.messages.PlotOptions` and need the same
figure-to-PNG-to-S3 pipeline, so that logic lives here rather than being duplicated.
"""

import io

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt  # noqa: E402
import seaborn as sns  # noqa: E402
from matplotlib.axes import Axes  # noqa: E402
from matplotlib.figure import Figure  # noqa: E402

from helix_core.messages import PlotOptions  # noqa: E402
from helix_core.storage import Storage  # noqa: E402


def use_headless_backend() -> None:
    """Force matplotlib's non-interactive ``Agg`` backend.

    There is no display in a container, so every module that plots must call this
    (or import this module, which calls it at import time) before touching pyplot.
    """
    matplotlib.use("Agg")


def apply_style(opts: PlotOptions) -> None:
    """Apply shared seaborn/matplotlib styling from a :class:`PlotOptions`.

    Args:
        opts: The styling options to apply: seaborn theme (``style``, ``font_family``)
            and matplotlib rcParams for font sizes.
    """
    sns.set_theme(style=opts.style, font=opts.font_family)
    plt.rcParams["axes.titlesize"] = opts.title_font_size
    plt.rcParams["axes.labelsize"] = opts.axis_font_size
    plt.rcParams["xtick.labelsize"] = opts.tick_size
    plt.rcParams["ytick.labelsize"] = opts.tick_size


def finish_axes(
    ax: Axes,
    opts: PlotOptions,
    title: str,
    x_label: str | None,
    y_label: str | None,
) -> None:
    """Apply title, axis labels and tick styling to an axes.

    Args:
        ax: The axes to style.
        opts: Styling options. ``opts.title``, when set, overrides ``title``. Tick
            label size and rotation come from ``opts.tick_size``,
            ``opts.rotate_x_labels`` and ``opts.rotate_y_labels``.
        title: The default title, used unless ``opts.title`` is set.
        x_label: The x-axis label, or ``None`` to leave it unset.
        y_label: The y-axis label, or ``None`` to leave it unset.
    """
    ax.set_title(opts.title or title, fontsize=opts.title_font_size)
    if x_label is not None:
        ax.set_xlabel(x_label, fontsize=opts.axis_font_size)
    if y_label is not None:
        ax.set_ylabel(y_label, fontsize=opts.axis_font_size)
    ax.tick_params(axis="x", labelsize=opts.tick_size, rotation=opts.rotate_x_labels)
    ax.tick_params(axis="y", labelsize=opts.tick_size, rotation=opts.rotate_y_labels)


def figure_to_png(fig: Figure, dpi: int) -> bytes:
    """Render a figure to PNG bytes and close it.

    Args:
        fig: The figure to render. Closed before returning, regardless of errors in
            rendering it (unless rendering itself raises).
        dpi: The resolution, in dots per inch, to render at.

    Returns:
        The figure's content as PNG bytes.
    """
    buffer = io.BytesIO()
    try:
        fig.savefig(buffer, format="png", dpi=dpi, bbox_inches="tight")
    finally:
        plt.close(fig)
    return buffer.getvalue()


def save_figure(storage: Storage, key: str, fig: Figure, dpi: int) -> int:
    """Render a figure to PNG and upload it to S3.

    Args:
        storage: The storage client to upload to.
        key: The S3 key to write the PNG to.
        fig: The figure to render and upload. Closed as a side effect.
        dpi: The resolution, in dots per inch, to render at.

    Returns:
        The size of the uploaded PNG, in bytes.
    """
    png = figure_to_png(fig, dpi)
    storage.put_bytes(key, png, "image/png")
    return len(png)
