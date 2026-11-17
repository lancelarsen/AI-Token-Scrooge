"""SCROOGE_DATA_DIR resolution and the sign-in launch entry it produces."""
import os
import shlex
import sys
from pathlib import Path

import pytest

from token_scrooge.infra.cli import DATA_DIR_ENV, default_data_dir, resolve_data_dir


def test_option_overrides_environment(tmp_path):
    chosen, source = resolve_data_dir(tmp_path / "opt", {DATA_DIR_ENV: str(tmp_path / "env")})
    assert (chosen, source) == ((tmp_path / "opt").resolve(), "option")


def test_environment_used_when_no_option(tmp_path):
    chosen, source = resolve_data_dir(None, {DATA_DIR_ENV: str(tmp_path / "env")})
    assert (chosen, source) == ((tmp_path / "env").resolve(), "environment")


def test_blank_environment_falls_back_to_default():
    assert resolve_data_dir(None, {DATA_DIR_ENV: "  "}) == (default_data_dir(), "default")
    assert resolve_data_dir(None, {}) == (default_data_dir(), "default")


def test_environment_expands_home_and_becomes_absolute(monkeypatch, tmp_path):
    monkeypatch.setenv("HOME", str(tmp_path))
    chosen, _ = resolve_data_dir(None, {DATA_DIR_ENV: "~/scrooge"})
    assert chosen == (tmp_path / "scrooge").resolve() and chosen.is_absolute()
    monkeypatch.chdir(tmp_path)
    chosen, _ = resolve_data_dir(None, {DATA_DIR_ENV: "relative"})
    assert chosen == (tmp_path / "relative").resolve()


@pytest.mark.skipif(sys.platform == "darwin", reason="Linux autostart entry")
def test_autostart_entry_names_environment_folder(monkeypatch, tmp_path):
    from token_scrooge.app import desktop
    monkeypatch.setenv("XDG_CONFIG_HOME", str(tmp_path / "config"))
    folder = tmp_path / "data with space"
    desktop.set_launch_at_login(True, folder)
    entry = (tmp_path / "config/autostart/ai-token-scrooge.desktop").read_text()
    exec_line = next(line for line in entry.splitlines() if line.startswith("Exec="))
    args = shlex.split(exec_line[5:])
    assert args[-3:] == ["--background", "--data-dir", str(folder)]
    desktop.set_launch_at_login(True)
    entry = (tmp_path / "config/autostart/ai-token-scrooge.desktop").read_text()
    assert "--data-dir" not in entry
    desktop.set_launch_at_login(False)
    assert not (tmp_path / "config/autostart/ai-token-scrooge.desktop").exists()
