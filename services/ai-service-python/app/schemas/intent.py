"""
Structured output schemas for OpenAI Structured Outputs (2024).

These JSON schemas enforce exact response structure at the API level,
eliminating parsing failures and ensuring deterministic output format.
This replaces the weaker ``response_format: json_object`` approach.

References:
  - OpenAI Structured Outputs: https://platform.openai.com/docs/guides/structured-outputs
  - Google Cloud Text-to-SQL best practices (intent-first pipeline)
  - NIST AI RMF Measure 2.2: system evaluated for trustworthiness
"""

from typing import Optional

from pydantic import BaseModel, Field


# ────────────────────────────────────────────────────────────
# Pydantic models
# ────────────────────────────────────────────────────────────


class IntentFilter(BaseModel):
    """A single filter condition extracted from the user's question."""

    column: str
    operator: str  # =, >, <, >=, <=, !=, LIKE, IN, BETWEEN, IS NULL, IS NOT NULL
    value: Optional[str] = None


class ExtractedIntent(BaseModel):
    """
    Structured intent extracted from a natural language analytics question.

    This is the output of the *intent extraction step* — a lightweight,
    deterministic LLM call that precedes SQL generation.  It identifies
    what the user wants so the pipeline can filter the schema and provide
    focused context to the SQL generation step.
    """

    queryType: str = Field(
        description=(
            "Semantic query type: count | list | aggregation | ranking | "
            "filter | join | group | comparison | time_based | unknown"
        )
    )
    relevantTables: list[str] = Field(default_factory=list)
    relevantColumns: list[str] = Field(default_factory=list)
    filters: list[IntentFilter] = Field(default_factory=list)
    aggregations: list[str] = Field(default_factory=list)
    orderBy: Optional[str] = None
    limitHint: Optional[int] = None
    isAnswerable: bool = True
    confidence: float = Field(ge=0, le=1, default=0.0)
    reasoning: str = ""


# ────────────────────────────────────────────────────────────
# JSON Schemas for OpenAI Structured Outputs  (strict mode)
#
# With  strict: true  the API guarantees the LLM output matches
# the schema exactly — no malformed JSON, no missing keys.
# ────────────────────────────────────────────────────────────


INTENT_EXTRACTION_SCHEMA: dict = {
    "name": "intent_extraction",
    "strict": True,
    "schema": {
        "type": "object",
        "properties": {
            "queryType": {
                "type": "string",
                "description": (
                    "Semantic query type: count, list, aggregation, ranking, "
                    "filter, join, group, comparison, time_based, unknown"
                ),
            },
            "relevantTables": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Table names from the provided schema that are relevant",
            },
            "relevantColumns": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Column names relevant to the question (table.column or column)",
            },
            "filters": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "column": {"type": "string"},
                        "operator": {"type": "string"},
                        "value": {
                            "anyOf": [{"type": "string"}, {"type": "null"}]
                        },
                    },
                    "required": ["column", "operator", "value"],
                    "additionalProperties": False,
                },
                "description": "Filter conditions implied by the question",
            },
            "aggregations": {
                "type": "array",
                "items": {"type": "string"},
                "description": "Aggregation functions needed: COUNT, SUM, AVG, MIN, MAX, GROUP BY",
            },
            "orderBy": {
                "anyOf": [{"type": "string"}, {"type": "null"}],
                "description": "Sort preference if any (e.g. 'price DESC')",
            },
            "limitHint": {
                "anyOf": [{"type": "integer"}, {"type": "null"}],
                "description": "Suggested row limit if mentioned in query",
            },
            "isAnswerable": {
                "type": "boolean",
                "description": "Whether the question can be answered from the provided schema",
            },
            "confidence": {
                "type": "number",
                "description": "Confidence score between 0.0 and 1.0",
            },
            "reasoning": {
                "type": "string",
                "description": "Brief justification of the intent analysis",
            },
        },
        "required": [
            "queryType",
            "relevantTables",
            "relevantColumns",
            "filters",
            "aggregations",
            "orderBy",
            "limitHint",
            "isAnswerable",
            "confidence",
            "reasoning",
        ],
        "additionalProperties": False,
    },
}


SQL_GENERATION_SCHEMA: dict = {
    "name": "sql_generation",
    "strict": True,
    "schema": {
        "type": "object",
        "properties": {
            "sqlProposal": {
                "type": "object",
                "properties": {
                    "dialect": {"type": "string"},
                    "sql": {
                        "type": "string",
                        "description": "The generated read-only PostgreSQL SELECT query",
                    },
                    "parameters": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "value": {
                                    "anyOf": [
                                        {"type": "string"},
                                        {"type": "number"},
                                        {"type": "boolean"},
                                        {"type": "null"},
                                    ]
                                },
                            },
                            "required": ["name", "value"],
                            "additionalProperties": False,
                        },
                    },
                },
                "required": ["dialect", "sql", "parameters"],
                "additionalProperties": False,
            },
            "explanationMetadata": {
                "type": "object",
                "properties": {
                    "intentSummary": {"type": "string"},
                    "reasoningSummary": {"type": "string"},
                    "selectedTables": {
                        "type": "array",
                        "items": {"type": "string"},
                    },
                    "selectedColumns": {
                        "type": "array",
                        "items": {"type": "string"},
                    },
                    "assumptions": {
                        "type": "array",
                        "items": {"type": "string"},
                    },
                    "confidenceScore": {"type": "number"},
                    "warnings": {
                        "type": "array",
                        "items": {"type": "string"},
                    },
                },
                "required": [
                    "intentSummary",
                    "reasoningSummary",
                    "selectedTables",
                    "selectedColumns",
                    "assumptions",
                    "confidenceScore",
                    "warnings",
                ],
                "additionalProperties": False,
            },
        },
        "required": ["sqlProposal", "explanationMetadata"],
        "additionalProperties": False,
    },
}
