"""Compatibility entry point for the current reproducible PBR texture preparation."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name('prepare_pbr_assets.py')), run_name='__main__')
