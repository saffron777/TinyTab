"""Genera los vectores dorados de paridad desde Sigma para el motor C#.

Uso (con el venv de Sigma, que tiene scipy/statsmodels/scikit-learn):

    D:\\IA\\Sigma\\.venv\\Scripts\\python.exe generate.py ^
        --sigma-root D:\\IA\\Sigma ^
        --out ..\\..\\tests\\StatisticEngine.Domain.Tests\\Extensions\\vectors

Escribe un vector por módulo de Sigma (``pruebas.json``, ``series.json``…) con el ejemplo
declarado de cada análisis y los casos de error de validación, más ``fixtures.json`` (los
datasets de prueba) y ``extensions-catalog.json`` (metadatos de roles y parámetros).

Sigma es sólo un oráculo de desarrollo: el servicio .NET no ejecuta Python. Los archivos
generados se versionan y las pruebas C# los consumen sin red.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any

EXCLUDED_MODULES = {"aprendizaje", "sem"}

# Análisis con aleatoriedad (NumPy) cuyo resultado no puede coincidir bit a bit con el RNG xoshiro
# del motor C#: se marcan para que las pruebas usen propiedades en lugar de igualdad.
NONDETERMINISTIC = {
    ("remuestreo", "bootstrap"),
    ("remuestreo", "permutation"),
    ("remuestreo", "monte_carlo"),
    ("remuestreo", "random_data"),
    ("transformar", "sample"),
    ("transformar", "partition"),
    ("modelos", "regularized"),
    ("doe", "factorial"),
    ("doe", "response_surface"),
}

# Id de Sigma -> id del catálogo C#. Sin entrada se usa el id de Sigma con guiones.
CSHARP_IDS = {
    ("pruebas", "anderson"): "anderson-darling",
    ("pruebas", "ks_one"): "ks-one-sample",
    ("pruebas", "ks_two"): "ks-two-samples",
    ("pruebas", "variances"): "variance-equality-tests",
    ("pruebas", "mood_median"): "mood-median",
    ("pruebas", "cochran_q"): "cochran-q",
    ("pruebas", "poisson_rate"): "poisson-rate",
    ("pruebas", "odds_ratio"): "odds-ratio",
    ("pruebas", "cmh"): "mantel-haenszel",
    ("pruebas", "partial_corr"): "partial-correlation",
    ("pruebas", "ci_one"): "confidence-interval",
    ("pruebas", "hotelling"): "hotelling-t2",
    ("pruebas", "jarque_bera"): "jarque-bera",
    ("potencia", "corr_power"): "power-correlation",
    ("potencia", "tost_power"): "power-tost",
    ("modelos", "ordinal"): "regression-ordinal",
    ("modelos", "multinomial"): "regression-multinomial",
    ("modelos", "count"): "regression-count",
    ("modelos", "glm"): "regression-glm",
    ("modelos", "quantile"): "regression-quantile",
    ("modelos", "robust"): "regression-robust",
    ("modelos", "wls"): "regression-wls",
    ("modelos", "polynomial"): "regression-polynomial",
    ("modelos", "nonlinear"): "regression-nonlinear",
    ("modelos", "regularized"): "regression-regularized",
    ("modelos", "iv"): "regression-iv",
    ("modelos", "mixed"): "regression-mixed",
    ("modelos", "diagnostics"): "regression-diagnostics",
    ("modelos", "gam"): "regression-gam",
    ("modelos", "pls"): "regression-pls",
    ("modelos", "loglinear"): "regression-loglinear",
    ("anova_avanzado", "glm_anova"): "anova-factorial",
    ("anova_avanzado", "rm_anova"): "anova-repeated-measures",
    ("anova_avanzado", "manova"): "anova-manova",
    ("anova_avanzado", "nested"): "anova-nested",
    ("anova_avanzado", "posthoc"): "anova-posthoc",
    ("encuestas", "survey_ols"): "survey-ols",
    ("encuestas", "survey_logistic"): "survey-logistic",
}

# Capacidades de Sigma ya cubiertas por un análisis baseline (no se duplican).
COVERED = {
    ("pruebas", "sign_test"),
    ("pruebas", "runs"),
    ("pruebas", "tost"),
    ("potencia", "t_power"),
    ("potencia", "prop_power"),
    ("potencia", "anova_power"),
    ("modelos", "stepwise"),
    ("modelos", "best_subsets"),
    ("modelos", "binary"),
}

# Capacidades parcialmente cubiertas: un mismo spec de Sigma alimenta un id C# propio con
# parámetros fijados (no entran en la comparación de metadatos).
EXTRA_CASES = (
    {
        "module": "potencia",
        "sigma_id": "t_power",
        "csharp_id": "power-paired-t",
        "params": {"design": "paired", "solve_for": "n", "effect_size": 0.5, "power": 0.8},
    },
    {
        "module": "potencia",
        "sigma_id": "prop_power",
        "csharp_id": "power-prop2",
        "params": {"design": "two", "solve_for": "n", "p0": 0.5, "p1": 0.6, "power": 0.8},
    },
    {
        "module": "modelos",
        "sigma_id": "binary",
        "csharp_id": "regression-probit",
        "params": {"link": "probit"},
    },
)


def csharp_id(module: str, sigma_id: str) -> str:
    return CSHARP_IDS.get((module, sigma_id), sigma_id.replace("_", "-"))


# Sustituciones de parámetros para el caso de ejemplo cuando el ejemplo de Sigma ejercita una
# variante que aún no se ha portado (el catálogo de parámetros se conserva intacto).
EXAMPLE_OVERRIDES = {
    ("modelos", "robust"): {"method": "hc3"},
    ("modelos", "count"): {"family": "poisson"},
    ("modelos", "diagnostics"): {"tests": ["breusch_pagan", "durbin_watson"]},
    ("anova_avanzado", "posthoc"): {"method": "bonferroni"},
    ("series", "filters"): {"filter": "hp"},
    ("series", "decompose"): {"method": "classical_add"},
    ("series", "smoothing"): {"method": "ses", "horizon": 6, "holdout": 0},
    ("series", "backtest"): {"methods": ["naive", "snaive"], "origins": 3, "horizon": 6},
    ("econometria", "copula"): {"family": "gaussian", "simulate": 0},
    ("multivariado", "mahalanobis"): {"method": "classical"},
    ("graficos", "heatmap"): {"method": "spearman", "cluster": False},
}


def plain(value: Any) -> Any:
    """Convierte valores de Sigma/NumPy/pandas a JSON plano (no finitos como textos)."""
    import numpy as np
    import pandas as pd

    from sigma.domain.analysis.output import Cell, P

    if isinstance(value, Cell):
        return plain(value.value)
    if isinstance(value, P):
        return plain(float(value))
    if value is None:
        return None
    if isinstance(value, (np.bool_, bool)):
        return bool(value)
    if isinstance(value, (np.integer,)):
        return int(value)
    if isinstance(value, (np.floating, float)):
        number = float(value)
        if math.isnan(number):
            return "NaN"
        if math.isinf(number):
            return "Infinity" if number > 0 else "-Infinity"
        if number == 0 and math.copysign(1, number) < 0:
            return "-0"
        return number
    if isinstance(value, pd.Timestamp):
        return value.strftime("%Y-%m-%d") if value == value.normalize() else value.strftime("%Y-%m-%dT%H:%M:%S")
    if isinstance(value, np.datetime64):
        return plain(pd.Timestamp(value))
    if isinstance(value, np.ndarray):
        return [plain(item) for item in value.tolist()]
    if isinstance(value, (list, tuple)):
        return [plain(item) for item in value]
    if isinstance(value, dict):
        return {str(key): plain(item) for key, item in value.items()}
    if isinstance(value, (str, int)):
        return value
    return str(value)


def frame_columns(frame) -> list[dict[str, Any]]:
    """Columnas tipadas de un DataFrame de pandas, con null para los faltantes."""
    import pandas as pd

    columns: list[dict[str, Any]] = []
    for name in frame.columns:
        series = frame[name]
        if pd.api.types.is_datetime64_any_dtype(series):
            columns.append(
                {
                    "name": str(name),
                    "kind": "datetime",
                    "values": [None if pd.isna(v) else plain(v) for v in series],
                }
            )
        elif pd.api.types.is_numeric_dtype(series) or pd.api.types.is_bool_dtype(series):
            columns.append(
                {
                    "name": str(name),
                    "kind": "numeric",
                    "values": [None if pd.isna(v) else plain(float(v)) for v in series],
                }
            )
        else:
            columns.append(
                {
                    "name": str(name),
                    "kind": "text",
                    "values": [None if pd.isna(v) else str(v) for v in series],
                }
            )
    return columns


def chart_plain(spec: Any) -> dict[str, Any]:
    """Normaliza una figura Plotly a los datos que interesan a la paridad."""
    if not isinstance(spec, dict):
        return {"raw": plain(spec)}
    traces = []
    for trace in spec.get("data", []):
        item = {"type": plain(trace.get("type")) if trace.get("type") else None, "name": plain(trace.get("name"))}
        for key in ("x", "y", "z", "values", "labels", "text", "ids"):
            if key in trace:
                item[key] = plain(trace[key])
        traces.append(item)
    layout = spec.get("layout") or {}
    title = layout.get("title")
    if isinstance(title, dict):
        title = title.get("text")
    return {"title": plain(title), "traces": traces}


def output_plain(output: Any) -> dict[str, Any]:
    """Serializa el AnalysisOutput de Sigma (bloques, datasets y números clave)."""
    blocks = []
    for block in output.blocks:
        kind = block.get("type")
        if kind == "table":
            blocks.append(
                {
                    "type": "table",
                    "title": plain(block.get("title")),
                    "columns": [plain(column) for column in block.get("columns", [])],
                    "rows": [[plain(cell) for cell in row] for row in block.get("rows", [])],
                    "note": plain(block.get("note")),
                }
            )
        elif kind == "tiles":
            blocks.append(
                {
                    "type": "tiles",
                    "items": [[plain(item) for item in item_tuple] for item_tuple in block.get("items", [])],
                }
            )
        elif kind == "text":
            blocks.append({"type": "text", "paragraphs": [plain(text) for text in block.get("paragraphs", [])]})
        elif kind == "alert":
            blocks.append({"type": "alert", "severity": plain(block.get("kind")), "message": plain(block.get("message"))})
        elif kind == "heading":
            blocks.append({"type": "heading", "text": plain(block.get("text"))})
        elif kind == "chart":
            blocks.append({"type": "chart", "caption": plain(block.get("caption")), "spec": chart_plain(block.get("spec"))})
        else:
            blocks.append({"type": str(kind), "raw": plain(block)})

    datasets = {}
    for key, offer in output.datasets.items():
        datasets[key] = {"label": plain(offer.label), "columns": frame_columns(offer.frame)}

    return {
        "title": plain(output.title),
        "subtitle": plain(output.subtitle),
        "blocks": blocks,
        "datasets": datasets,
        "result": plain(output.result),
    }


def catalog_entry(spec: Any) -> dict[str, Any]:
    """Metadatos declarativos del análisis para comparar el catálogo C#."""
    roles = []
    for role in spec.roles:
        roles.append(
            {
                "name": role.name,
                "label": role.label,
                "kind": role.kind,
                "multiple": role.multiple,
                "optional": role.optional,
                "min_count": role.min_count,
                "max_count": role.max_count,
                "help": role.help,
                "dataset_param": role.dataset_param,
            }
        )
    params = []
    for field in spec.params:
        params.append(
            {
                "name": field.name,
                "label": field.label,
                "kind": field.kind,
                "default": plain(field.default),
                "options": [[plain(value), plain(label)] for value, label in field.options],
                "help": field.help,
                "optional": field.optional,
                "minimum": field.minimum,
                "maximum": field.maximum,
                "visible_if": (
                    None
                    if field.visible_if is None
                    else [field.visible_if[0], [str(value) for value in field.visible_if[1]]]
                ),
            }
        )
    outputs = [{"key": item.key, "label": item.label, "required": item.required} for item in spec.outputs]
    return {
        "module": spec.module,
        "id": spec.id,
        "csharp_id": csharp_id(spec.module, spec.id),
        "covered": (spec.module, spec.id) in COVERED,
        "label": spec.label,
        "group": spec.group,
        "description": spec.description,
        "roles": roles,
        "params": params,
        "outputs": outputs,
        "heavy": spec.heavy,
        "needs_dataset": spec.needs_dataset,
    }


def error_cases(module: str, spec: Any, frame, run, case_id: str) -> list[dict[str, Any]]:
    """Casos de validación: cada rol obligatorio omitido y un select inválido."""
    from sigma.domain.errors import InvalidInputError

    cases: list[dict[str, Any]] = []
    example = spec.example or {"roles": {}, "params": {}}
    roles = dict(example.get("roles") or {})
    params = dict(example.get("params") or {})

    for role in spec.roles:
        if role.optional:
            continue
        if role.name not in roles:
            continue
        broken = dict(roles)
        broken.pop(role.name, None)
        try:
            run(module, spec.id, frame, broken, params)
        except InvalidInputError as error:
            cases.append(
                {"kind": "error", "id": case_id, "sigma_id": spec.id, "roles": broken, "params": params, "error": str(error)}
            )
        except Exception:  # noqa: BLE001 - un fallo de cálculo no es un caso de validación
            continue

    for field in spec.params:
        if field.kind != "select" or not field.options:
            continue
        broken = dict(params)
        broken[field.name] = "__valor_invalido__"
        try:
            run(module, spec.id, frame, roles, broken)
        except InvalidInputError as error:
            cases.append(
                {
                    "kind": "error",
                    "id": case_id,
                    "sigma_id": spec.id,
                    "roles": roles,
                    "params": broken,
                    "error": str(error),
                }
            )
            break
        except Exception:  # noqa: BLE001
            continue

    return cases


def main() -> int:
    parser = argparse.ArgumentParser(description="Genera vectores dorados desde Sigma.")
    parser.add_argument("--sigma-root", required=True, type=Path, help="Raíz del checkout de Sigma.")
    parser.add_argument("--out", required=True, type=Path, help="Directorio de vectores de salida.")
    parser.add_argument("--modules", nargs="*", default=None, help="Módulos concretos (por defecto todos los incluidos).")
    args = parser.parse_args()

    sigma_root = args.sigma_root.resolve()
    out_dir = args.out.resolve()
    sys.path.insert(0, str(sigma_root))
    out_dir.mkdir(parents=True, exist_ok=True)

    import numpy as np
    import pandas as pd
    import scipy
    import statsmodels
    import sklearn

    from sigma.domain.analysis import registry
    from sigma.domain.analysis.modules import CATALOG_MODULES
    from tests.analysis_data import lookup_frame, more_rows, run, standard_frame

    try:
        commit = (sigma_root / ".git" / "HEAD").read_text(encoding="utf-8").strip()
        if commit.startswith("ref: "):
            commit = (sigma_root / ".git" / commit[5:]).read_text(encoding="utf-8").strip()
    except OSError:
        commit = "desconocido"

    registry.load_all()
    modules = [
        module.key
        for module in CATALOG_MODULES
        if module.key not in EXCLUDED_MODULES and (not args.modules or module.key in args.modules)
    ]

    frame = standard_frame()
    fixtures = {
        "sigmaCommit": commit,
        "standard": {"label": "standard_frame", "columns": frame_columns(frame)},
        "lookup": {"label": "lookup_frame", "columns": frame_columns(lookup_frame())},
        "more": {"label": "more_rows", "columns": frame_columns(more_rows())},
    }
    (out_dir / "fixtures.json").write_text(
        json.dumps(fixtures, ensure_ascii=False, indent=1), encoding="utf-8"
    )

    catalog: list[dict[str, Any]] = []
    for module in modules:
        specs = registry.catalog(module)
        cases: list[dict[str, Any]] = []
        for spec in specs:
            catalog.append(catalog_entry(spec))
            if (module, spec.id) in COVERED:
                continue
            case_id = csharp_id(module, spec.id)
            example = spec.example or {"roles": {}, "params": {}}
            example_params = {**(example.get("params") or {}), **EXAMPLE_OVERRIDES.get((module, spec.id), {})}
            deterministic = (module, spec.id) not in NONDETERMINISTIC
            try:
                output = run(module, spec.id, frame, example.get("roles"), example_params)
                expected = output_plain(output)
            except Exception as error:  # noqa: BLE001 - se documenta el fallo del propio Sigma
                expected = {"error": f"{type(error).__name__}: {error}"}

            cases.append(
                {
                    "kind": "example",
                    "id": case_id,
                    "sigma_id": spec.id,
                    "deterministic": deterministic,
                    "roles": example.get("roles") or {},
                    "params": example_params,
                    "frame": "standard",
                    "expected": expected,
                }
            )
            cases.extend(error_cases(module, spec, frame, run, case_id))

        for extra in EXTRA_CASES:
            if extra["module"] != module:
                continue
            spec = registry.get(module, extra["sigma_id"])
            example = spec.example or {"roles": {}, "params": {}}
            params = {**(example.get("params") or {}), **extra["params"]}
            try:
                output = run(module, spec.id, frame, example.get("roles"), params)
                expected = output_plain(output)
            except Exception as error:  # noqa: BLE001
                expected = {"error": f"{type(error).__name__}: {error}"}
            cases.append(
                {
                    "kind": "example",
                    "id": extra["csharp_id"],
                    "sigma_id": spec.id,
                    "deterministic": (module, spec.id) not in NONDETERMINISTIC,
                    "roles": example.get("roles") or {},
                    "params": params,
                    "frame": "standard",
                    "expected": expected,
                    "catalog": False,
                }
            )

        (out_dir / f"{module}.json").write_text(
            json.dumps(
                {
                    "module": module,
                    "sigmaCommit": commit,
                    "python": sys.version.split()[0],
                    "numpy": np.__version__,
                    "pandas": pd.__version__,
                    "scipy": scipy.__version__,
                    "statsmodels": statsmodels.__version__,
                    "sklearn": sklearn.__version__,
                    "cases": cases,
                },
                ensure_ascii=False,
                indent=1,
            ),
            encoding="utf-8",
        )
        print(f"{module}: {len(specs)} análisis, {len(cases)} casos")

    (out_dir / "extensions-catalog.json").write_text(
        json.dumps(
            {
                "sigmaCommit": commit,
                "modules": modules,
                "analyses": catalog,
            },
            ensure_ascii=False,
            indent=1,
        ),
        encoding="utf-8",
    )
    print(f"Catálogo: {len(catalog)} análisis en {len(modules)} módulos")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
