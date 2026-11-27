#!/usr/bin/env python3
"""Compiler project graph plus source import guards. Run from any directory."""

from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "backend/src"
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
        text = re.sub(r"(?m)^\s*//.*$", "", source.read_text(encoding="utf-8"))
        if inner:
            require(
                not re.search(
                    r"\b(Microsoft\.(AspNetCore|EntityFrameworkCore|Extensions)|MongoDB|Npgsql|MassTransit|HttpContext|ClaimsPrincipal|IServiceProvider|HttpClient)\b",
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


if errors:
    raise SystemExit("\n".join(errors))
print(f"Architecture checks passed for {len(projects)} backend projects.")
