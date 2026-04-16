"""
Prompt construction for the two-step AI pipeline.

Step 1 — Intent Extraction:
  build_intent_system_prompt()   system-level instructions
  build_intent_prompt()          user-level prompt with question + schema summary

Step 2 — SQL Generation:
  build_system_prompt()          safety-focused system prompt (no JSON format
                                 instructions — that is now enforced by
                                 OpenAI Structured Outputs)
  build_prompt()                 user-level prompt with question + schema +
                                 optional intent context

Helper:
  build_schema_summary()         compact schema representation for intent step
"""

from typing import Optional

from app.schemas.generate_sql import GenerateSqlRequest, SchemaMetadata
from app.schemas.intent import ExtractedIntent


# ────────────────────────────────────────────────────────────
# Step 1: Intent Extraction prompts
# ────────────────────────────────────────────────────────────


def build_intent_system_prompt() -> str:
    """System prompt for the intent extraction step."""
    return (
        "You are an intent extraction agent for a PostgreSQL analytics system.\n"
        "Your task is to analyse a natural language question and extract "
        "structured intent: which tables and columns are relevant, what type "
        "of query is needed, any filters, aggregations, or sort/limit hints.\n"
        "The user may ask questions in Dutch or English.\n"
        "You must only reference tables and columns that exist in the "
        "provided schema summary.\n"
        "If the question cannot be answered from the schema, set "
        "isAnswerable to false and explain why in reasoning."
    )


def build_intent_prompt(question: str, schema_summary: str) -> str:
    """User prompt for intent extraction."""
    return (
        f"Question: {question}\n\n"
        f"Available database schema:\n{schema_summary}\n\n"
        "Extract the structured intent for this question."
    )


def build_schema_summary(schema: SchemaMetadata) -> str:
    """
    Build a compact, token-efficient schema summary for the intent step.

    Format:
      - customers (id, name, email, country, created_at)
      - orders (id, customer_id, product_id, quantity, total_amount, status, order_date)
      Relationships:
      - orders.customer_id → customers.id

    This is deliberately more compact than the full schema (no data types,
    no descriptions) because the intent step only needs to know *what exists*,
    not the exact column types.
    """
    lines = []
    for table in schema.tables:
        cols = ", ".join(col.columnName for col in table.columns)
        lines.append(f"- {table.tableName} ({cols})")

    if schema.relationships:
        lines.append("Relationships:")
        for rel in schema.relationships:
            lines.append(
                f"- {rel.fromTable}.{rel.fromColumn} → {rel.toTable}.{rel.toColumn}"
            )

    return "\n".join(lines)


# ────────────────────────────────────────────────────────────
# Step 2: SQL Generation prompts
# ────────────────────────────────────────────────────────────


def build_system_prompt() -> str:
    """
    System prompt for SQL generation.

    Note: JSON output format instructions have been *removed* from this
    prompt.  Output structure is now enforced by OpenAI Structured Outputs
    (``response_format: json_schema`` with ``strict: true``), which
    guarantees the returned JSON matches the schema exactly.

    This prompt now focuses purely on safety and behavioural constraints.
    """
    return (
        "You are a PostgreSQL read-only SQL generator.\n"
        "Safety constraints:\n"
        "1) Only SELECT queries are allowed.\n"
        "2) Never generate schema/data modification commands "
        "(INSERT, UPDATE, DELETE, DROP, ALTER, TRUNCATE, CREATE, "
        "GRANT, REVOKE, COPY).\n"
        "3) Generate exactly one SQL statement (no multi-statement "
        "query, no semicolon chaining).\n"
        "4) Always include a LIMIT clause.\n"
        "5) Use only tables/columns provided in the schema metadata.\n"
        "6) If the question cannot be answered safely from the given "
        "schema, return a safe fallback SELECT with warnings."
    )


def build_prompt(
    request: GenerateSqlRequest,
    intent: Optional[ExtractedIntent] = None,
    filtered_schema: Optional[SchemaMetadata] = None,
) -> str:
    """
    User prompt for SQL generation.

    When an ``intent`` is provided (from Step 1), it is included as
    explicit context so the SQL generation LLM has a clearer picture
    of what to produce.

    When a ``filtered_schema`` is provided, it replaces the full schema
    in the prompt — reducing noise and token usage.
    """
    schema = filtered_schema if filtered_schema is not None else request.schemaMetadata

    # ── schema block ──
    table_lines = []
    for table in schema.tables:
        columns = ", ".join(
            f"{col.columnName}:{col.dataType}" for col in table.columns
        )
        table_lines.append(f"- {table.tableName} ({columns})")

    relationships = []
    for rel in schema.relationships:
        relationships.append(
            f"- {rel.fromTable}.{rel.fromColumn} -> "
            f"{rel.toTable}.{rel.toColumn}"
        )

    tables_text = "\n".join(table_lines) if table_lines else "- none"
    relationships_text = "\n".join(relationships) if relationships else "- none"

    # ── intent context block (new) ──
    intent_block = ""
    if intent is not None:
        intent_lines = [
            "Previously extracted intent for this question:",
            f"  Query type: {intent.queryType}",
            f"  Relevant tables: {', '.join(intent.relevantTables) or 'none identified'}",
            f"  Relevant columns: {', '.join(intent.relevantColumns) or 'none identified'}",
        ]
        if intent.filters:
            filters_str = "; ".join(
                f"{f.column} {f.operator} {f.value}" for f in intent.filters
            )
            intent_lines.append(f"  Filters: {filters_str}")
        if intent.aggregations:
            intent_lines.append(
                f"  Aggregations: {', '.join(intent.aggregations)}"
            )
        if intent.orderBy:
            intent_lines.append(f"  Order by: {intent.orderBy}")
        if intent.limitHint is not None:
            intent_lines.append(f"  Limit hint: {intent.limitHint}")
        intent_lines.append(
            f"  Answerable: {'yes' if intent.isAnswerable else 'no'}"
        )
        intent_lines.append(f"  Reasoning: {intent.reasoning}")
        intent_block = "\n".join(intent_lines) + "\n\n"

    return (
        f"{intent_block}"
        "Generate a read-only PostgreSQL query proposal for this request.\n"
        f"Natural language question: {request.naturalLanguageQuery}\n"
        "Use ONLY this schema metadata:\n"
        f"Schema tables:\n{tables_text}\n"
        f"Schema relationships:\n{relationships_text}\n"
    )
