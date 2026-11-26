import matplotlib.pyplot as plt
import pytest
from pydantic import ValidationError

from helix_core.messages import PlotOptions
from helix_core.plotting import figure_to_png, save_figure
from helix_core.storage import Storage

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def test_figure_to_png_returns_png_bytes_and_closes_the_figure() -> None:
    fig, ax = plt.subplots()
    ax.plot([0, 1], [0, 1])

    png = figure_to_png(fig, dpi=100)

    assert png.startswith(PNG_SIGNATURE)
    assert plt.get_fignums() == []


def test_save_figure_uploads_png_and_returns_its_size(s3_storage: Storage) -> None:
    fig, ax = plt.subplots()
    ax.plot([0, 1], [0, 1])

    size = save_figure(s3_storage, "experiments/e/eda/a.png", fig, dpi=100)

    uploaded = s3_storage.get_bytes("experiments/e/eda/a.png")
    assert uploaded.startswith(PNG_SIGNATURE)
    assert size == len(uploaded)
    assert plt.get_fignums() == []


def test_plot_options_rejects_an_invalid_style() -> None:
    with pytest.raises(ValidationError):
        PlotOptions(style="not-a-real-style")
