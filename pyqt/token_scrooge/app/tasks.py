"""Run blocking provider/database work off the UI thread and deliver results on it."""
from __future__ import annotations

import traceback
from concurrent.futures import Future, ThreadPoolExecutor
from typing import Any, Callable, Optional

from PyQt6.QtCore import QObject, pyqtSignal

_pool = ThreadPoolExecutor(max_workers=8, thread_name_prefix="scrooge")


class _Bridge(QObject):
    deliver = pyqtSignal(object, object)

    def __init__(self):
        super().__init__()
        self.deliver.connect(lambda callback, value: callback(value))


_bridge: Optional[_Bridge] = None


def init() -> None:
    """Create the bridge on the UI thread before any task completes."""
    global _bridge
    if _bridge is None:
        _bridge = _Bridge()


def run(fn: Callable[..., Any], *args, done: Optional[Callable[[Any], None]] = None,
        failed: Optional[Callable[[BaseException], None]] = None) -> Future:
    init()

    def complete(future: Future) -> None:
        error = future.exception()
        if error is not None:
            if failed:
                _bridge.deliver.emit(failed, error)
            else:
                traceback.print_exception(error)
        elif done:
            _bridge.deliver.emit(done, future.result())

    future = _pool.submit(fn, *args)
    future.add_done_callback(complete)
    return future


def post(callback: Callable[[Any], None], value: Any = None) -> None:
    """Invoke on the UI thread from any thread."""
    init()
    _bridge.deliver.emit(callback, value)


def shutdown() -> None:
    _pool.shutdown(wait=False, cancel_futures=True)
