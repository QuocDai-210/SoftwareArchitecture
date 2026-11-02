#!/usr/bin/env python3
"""Compiler project graph plus source import guards. Run from any directory."""

from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "backend/src"
FRONT = ROOT / "frontend/src"
errors = []


def require(condition, message):
    if not condition:
        errors.append(message)


shared = {
    name: SRC / f"Shared/SwiftRide.{name}/SwiftRide.{name}.csproj"
    for name in ("SharedKernel", "Contracts", "Application", "Infrastructure")
}
allowed = {
    shared["SharedKernel"]: set(),
    shared["Contracts"]: {shared["SharedKernel"]},
    shared["Application"]: {shared["SharedKernel"]},
    shared["Infrastructure"]: {shared["Contracts"], shared["Application"]},
    SRC / "ApiGateway/ApiGateway.csproj": {shared["Infrastructure"]},
}
for service in ("Trip", "Matching", "Payment"):
    layers = {
        layer: SRC / f"{service}Service/{layer}/{service}Service.{layer}.csproj"
        for layer in ("Domain", "Application", "Infrastructure", "Api")
    }
    allowed.update(
        {
            layers["Domain"]: {shared["SharedKernel"]},
            layers["Application"]: {layers["Domain"], shared["Application"], shared["Contracts"]},
            layers["Infrastructure"]: {layers["Application"], shared["Infrastructure"]},
            layers["Api"]: {layers["Application"], layers["Infrastructure"]},
        }
    )
projects = set(SRC.rglob("*.csproj"))
require(
    projects == set(allowed),
    "Unexpected/missing backend project; update explicit dependency graph.",
)
for project in sorted(projects):
    tree = ET.parse(project)
    refs = {
        (project.parent / ref.attrib["Include"]).resolve() for ref in tree.iter("ProjectReference")
    }
    require(
        refs == allowed.get(project, set()),
        f"{project.relative_to(ROOT)}: forbidden or missing project reference",
    )
    inner = project.parent.name in ("Domain", "Application") or project in (
        shared["SharedKernel"],
        shared["Application"],
        shared["Contracts"],
    )
    if inner:
        require(
            tree.getroot().attrib.get("Sdk") == "Microsoft.NET.Sdk",
            f"{project.name}: inner project must use plain SDK",
        )
        require(
            not list(tree.iter("PackageReference")) and not list(tree.iter("FrameworkReference")),
            f"{project.name}: framework/package in inner layer",
        )
    for source in project.parent.rglob("*.cs"):
        if {"bin", "obj"} & set(source.parts):
            continue
        text = re.sub(r"(?m)^\s*//.*$", "", source.read_text())
        if inner:
            require(
                not re.search(
                    r"\b(Microsoft\.(AspNetCore|EntityFrameworkCore|Extensions)|MongoDB|MassTransit|HttpContext|ClaimsPrincipal|IServiceProvider|HttpClient)\b",
                    text,
                ),
                f"{source.relative_to(ROOT)}: infrastructure leaks into inner layer",
            )
        if project.parent.name == "Api":
            require(
                not re.search(
                    r"\b(DbContext|DbSet|IMongoDatabase|IPublishEndpoint|GetRequiredService|ExecuteSql|SaveChangesAsync)\b|using (?:MassTransit|MongoDB|Microsoft.EntityFrameworkCore)",
                    text,
                ),
                f"{source.relative_to(ROOT)}: endpoint bypasses use case",
            )


def layer(path):
    parts = path.relative_to(FRONT).parts
    if parts[0] == "features":
        return parts[2]
    return parts[0]


def frontend_allowed(source, target):
    if source in ("domain", "application", "presentation", "infrastructure"):
        return (
            target
            in {
                "domain": {"domain"},
                "application": {"domain", "application"},
                "presentation": {"domain", "application", "presentation"},
                "infrastructure": {"domain", "application", "infrastructure"},
            }[source]
        )
    return source in ("app", "composition")


# Check guards themselves against reversed dependencies.
assert frontend_allowed("application", "domain")
assert not frontend_allowed("domain", "application")
assert not frontend_allowed("presentation", "infrastructure")
assert not frontend_allowed("infrastructure", "presentation")
assert frontend_allowed("composition", "infrastructure")

frontend_count = 0
for source in sorted(FRONT.rglob("*")):
    if source.suffix not in (".ts", ".tsx"):
        continue
    frontend_count += 1
    text = re.sub(r"(?m)^\s*//.*$", "", source.read_text())
    current = layer(source)
    imports = re.findall(r"(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s*)[\'\"]([^\'\"]+)[\'\"]", text)
    for name in imports:
        if name.startswith((".", "@/")):
            target = (FRONT / name[2:] if name.startswith("@/") else source.parent / name).resolve()
            require(target.is_relative_to(FRONT), f"{source.relative_to(ROOT)}: import escapes src")
            if target.is_relative_to(FRONT):
                require(
                    frontend_allowed(current, layer(target)),
                    f"{source.relative_to(ROOT)}: {current} imports {layer(target)} ({name})",
                )
        elif current in ("domain", "application"):
            require(False, f"{source.relative_to(ROOT)}: package {name} in inner layer")
    if current in ("domain", "application", "presentation"):
        require(
            not re.search(r"\b(fetch|XMLHttpRequest)\s*\(", text),
            f"{source.relative_to(ROOT)}: transport outside infrastructure",
        )
    if current in ("domain", "application"):
        require(
            not re.search(r"\b(window|document|localStorage|sessionStorage|process\.env)\b", text),
            f"{source.relative_to(ROOT)}: platform/UI leaks into inner layer",
        )

if errors:
    raise SystemExit("\n".join(errors))
print(
    f"Architecture checks passed: {len(projects)} backend projects, {frontend_count} frontend files."
)
