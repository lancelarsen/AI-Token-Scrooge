"""Plan labels, provider links and time-zone formatting."""
from __future__ import annotations

import os
import re
from datetime import datetime, timezone, tzinfo
from functools import lru_cache
from typing import Optional
from urllib.parse import quote
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError, available_timezones

from .model import ProviderAccount

_PLANS = {
    ("Devin", "enterprise"): "Enterprise",
    ("Claude", "pro"): "Pro", ("Claude", "max"): "Max", ("Claude", "team"): "Team", ("Claude", "enterprise"): "Enterprise",
    ("OpenAI", "free"): "Free", ("OpenAI", "go"): "Go", ("OpenAI", "plus"): "Plus", ("OpenAI", "pro"): "Pro",
    ("OpenAI", "prolite"): "Pro Lite", ("OpenAI", "promax"): "Pro Max", ("OpenAI", "team"): "Team",
    ("OpenAI", "business"): "Business", ("OpenAI", "enterprise"): "Enterprise", ("OpenAI", "edu"): "Edu",
    ("GitHub Copilot", "individual"): "Pro", ("GitHub Copilot", "individual_pro"): "Pro+",
    ("GitHub Copilot", "individual_max"): "Max", ("GitHub Copilot", "individual_edu"): "Education",
    ("GitHub Copilot", "business"): "Business", ("GitHub Copilot", "enterprise"): "Enterprise",
}


def plan_label(provider: str, plan: Optional[str]) -> str:
    return _PLANS.get((provider, plan), "Plan unavailable")


def usage_link(account: ProviderAccount) -> Optional[str]:
    p = account.Provider
    if p == "Devin" and re.fullmatch(r"[a-z0-9-]+\.devinenterprise\.com", account.Scope):
        return f"https://{account.Scope}/settings/my-analytics"
    if p == "Claude":
        return "https://claude.ai/new#settings/usage"
    if p == "OpenAI":
        return "https://chatgpt.com/settings/usage?tab=overview"
    if p == "GitHub Copilot" and account.Scope.lower().startswith("organization:"):
        return f"https://github.com/organizations/{quote(account.Scope[13:], safe='')}/settings/billing"
    if p == "GitHub Copilot":
        return "https://github.com/settings/billing"
    return None


def help_link(provider: str) -> Optional[str]:
    return {
        "Devin": "https://docs.devin.ai/enterprise/security-access/personal-analytics",
        "Claude": "https://support.claude.com/en/articles/11145838-use-claude-code-with-your-pro-or-max-plan",
        "OpenAI": "https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan",
        "GitHub Copilot": "https://docs.github.com/en/rest/billing/usage",
    }.get(provider)


def provider_display(provider: str) -> str:
    return "OpenAI Codex" if provider == "OpenAI" else provider


def capitalize_words(value: str) -> str:
    return re.sub(r"\b[a-z]", lambda m: m.group(0).upper(), value)


# ---------------------------------------------------------------- time zones

@lru_cache(maxsize=1)
def system_zone_name() -> str:
    """IANA name of the computer's zone (macOS and Linux both link /etc/localtime)."""
    env = os.environ.get("TZ", "").lstrip(":")
    if env and _valid(env):
        return env
    try:
        target = os.path.realpath("/etc/localtime")
        marker = "zoneinfo/"
        if marker in target:
            name = target.split(marker, 1)[1]
            if _valid(name):
                return name
    except OSError:
        pass
    try:
        with open("/etc/timezone", encoding="utf-8") as handle:
            name = handle.read().strip()
            if _valid(name):
                return name
    except OSError:
        pass
    return "UTC"


def _valid(name: str) -> bool:
    try:
        ZoneInfo(name)
        return True
    except (ZoneInfoNotFoundError, ValueError):
        return False


def resolve_zone(zone_id: Optional[str]) -> ZoneInfo:
    try:
        return ZoneInfo(zone_id) if zone_id else ZoneInfo(system_zone_name())
    except (ZoneInfoNotFoundError, ValueError):
        return ZoneInfo(system_zone_name())


@lru_cache(maxsize=1)
def zone_choices() -> list[str]:
    return sorted(z for z in available_timezones() if "/" in z and not z.startswith(("Etc/", "SystemV/", "posix/", "right/")))


def to_zone(value: datetime, zone: tzinfo) -> datetime:
    return value.astimezone(zone)


def _hour12(local: datetime) -> str:
    return f"{(local.hour % 12) or 12}:{local.minute:02d} {'AM' if local.hour < 12 else 'PM'}"


def format_reset(value: Optional[datetime], zone: tzinfo) -> str:
    """Equivalent to .NET "ddd, MMM d h:mm tt"."""
    if value is None:
        return "Not supplied by provider"
    local = value.astimezone(zone)
    return f"{local:%a, %b} {local.day} {_hour12(local)}"


def format_short(value: datetime, zone: tzinfo) -> str:
    """Equivalent to .NET "MMM d, h:mm tt"."""
    local = value.astimezone(zone)
    return f"{local:%b} {local.day}, {_hour12(local)}"


def format_time(value: datetime, zone: tzinfo) -> str:
    return _hour12(value.astimezone(zone))


def format_exact(value: Optional[datetime], zone: tzinfo) -> str:
    if value is None:
        return "Not supplied"
    return value.astimezone(zone).strftime("%Y-%m-%d %H:%M:%S %z")


def abbreviation(value: datetime, zone: tzinfo) -> str:
    name = value.astimezone(zone).tzname() or ""
    if re.fullmatch(r"[A-Z]{2,5}", name):
        return name
    offset = value.astimezone(zone).strftime("%z")
    return f"UTC{offset[:3]}:{offset[3:]}" if offset else "UTC"


def updated_label(observed: datetime, zone: tzinfo) -> str:
    local = observed.astimezone(zone)
    day = local.day
    suffix = "th" if day in (11, 12, 13) else {1: "st", 2: "nd", 3: "rd"}.get(day % 10, "th")
    return f"Updated {_hour12(local)} {abbreviation(observed, zone)}\n{local:%A, %b} {day}{suffix}"


def utcnow() -> datetime:
    return datetime.now(timezone.utc)
