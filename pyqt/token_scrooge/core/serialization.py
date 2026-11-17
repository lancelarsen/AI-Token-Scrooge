"""Dataclass <-> JSON conversion for persisted, non-secret state.

Enums are stored by name, datetimes as ISO-8601 UTC strings. Unknown keys from a
newer build are ignored and missing keys take the field default, so older saved
settings deserialize with deliberate defaults.
"""
from __future__ import annotations

import dataclasses
import enum
import json
import types
import typing
from datetime import datetime, timezone
from functools import lru_cache
from typing import Any, TypeVar, Union, get_args, get_origin

T = TypeVar("T")


def to_plain(value: Any) -> Any:
    if dataclasses.is_dataclass(value) and not isinstance(value, type):
        return {f.name: to_plain(getattr(value, f.name)) for f in dataclasses.fields(value)
                if not f.metadata.get("transient")}
    if isinstance(value, enum.Enum):
        return value.name
    if isinstance(value, datetime):
        return value.astimezone(timezone.utc).isoformat()
    if isinstance(value, (list, tuple)):
        return [to_plain(v) for v in value]
    if isinstance(value, dict):
        return {str(k): to_plain(v) for k, v in value.items()}
    return value


def dumps(value: Any) -> str:
    return json.dumps(to_plain(value), separators=(",", ":"), sort_keys=False)


@lru_cache(maxsize=None)
def _hints(cls: type) -> dict[str, Any]:
    return typing.get_type_hints(cls)


def from_plain(tp: Any, data: Any) -> Any:
    if data is None:
        return None
    origin = get_origin(tp)
    if origin in (Union, types.UnionType):
        args = [a for a in get_args(tp) if a is not type(None)]
        return from_plain(args[0], data) if len(args) == 1 else data
    if origin in (list, tuple, typing.Sequence, typing.MutableSequence) or tp in (list, tuple):
        (item,) = get_args(tp)[:1] or (Any,)
        values = [from_plain(item, v) for v in data]
        return tuple(values) if origin is tuple or tp is tuple else values
    if origin is dict:
        _, vt = get_args(tp)
        return {k: from_plain(vt, v) for k, v in data.items()}
    if isinstance(tp, type) and issubclass(tp, enum.Enum):
        return tp[data] if isinstance(data, str) else tp(data)
    if tp is datetime:
        parsed = datetime.fromisoformat(data)
        return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
    if isinstance(tp, type) and dataclasses.is_dataclass(tp):
        if not isinstance(data, dict):
            raise ValueError(f"Expected an object for {tp.__name__}.")
        hints = _hints(tp)
        kwargs = {}
        for f in dataclasses.fields(tp):
            if f.name in data and f.init:
                kwargs[f.name] = from_plain(hints[f.name], data[f.name])
        return tp(**kwargs)
    if tp is float and isinstance(data, int):
        return float(data)
    return data


def loads(tp: type[T] | Any, text: str) -> T:
    return from_plain(tp, json.loads(text))
