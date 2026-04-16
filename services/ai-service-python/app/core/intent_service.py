"""
Intent Extraction and Schema Filtering Service.

Implements a two-step AI engineering pattern inspired by:
  - Google Cloud Text-to-SQL best practices (intent → filtered schema → SQL)
  - OpenAI Structured Outputs (2024) for deterministic output format

Pipeline:
  Step 1  Extract structured intent from the natural language question.
  Step 2  Filter the database schema to only the tables/columns that
          are relevant to the extracted intent.
  Step 3  SQL generation with focused context (handled by GenerateSqlService).

Why this matters:
  - Reduces hallucination by limiting the LLM's context to relevant schema.
  - Makes the intent explicit and inspectable (observability / auditability).
  - Follows the "decompose complex tasks" principle from prompt engineering
    literature: a single LLM call for intent-analysis + SQL is harder
    than two smaller, focused calls.
"""

import logging
from typing import Optional

from app.core.llm_client import LlmClient
from app.core.prompting import (
    build_intent_system_prompt,
    build_intent_prompt,
    build_schema_summary,
)
from app.schemas.generate_sql import SchemaMetadata, TableMetadata
from app.schemas.intent import ExtractedIntent, INTENT_EXTRACTION_SCHEMA

logger = logging.getLogger(__name__)


class IntentService:
    """Extracts structured intent and filters schema for focused SQL generation."""

    def __init__(self, llm_client: LlmClient) -> None:
        self._llm_client = llm_client

    # ── Step 1: intent extraction ──────────────────────────────

    async def extract_intent(
        self,
        question: str,
        schema: SchemaMetadata,
    ) -> Optional[ExtractedIntent]:
        """
        Extract structured intent from a natural language question.

        Uses a lightweight LLM call with OpenAI Structured Outputs
        (``strict: true``) so the response is *guaranteed* to match
        the ``INTENT_EXTRACTION_SCHEMA``.

        Returns ``None`` when extraction fails — the caller should
        fall back to sending the full schema to the SQL generation step.
        """
        try:
            schema_summary = build_schema_summary(schema)
            system_prompt = build_intent_system_prompt()
            user_prompt = build_intent_prompt(question, schema_summary)

            result = await self._llm_client.generate_structured(
                prompt=user_prompt,
                system_prompt=system_prompt,
                json_schema=INTENT_EXTRACTION_SCHEMA,
                temperature=0.0,  # deterministic for reproducibility
            )

            intent = ExtractedIntent.model_validate(result)

            logger.info(
                "Intent extracted: type=%s tables=%s answerable=%s confidence=%.2f",
                intent.queryType,
                intent.relevantTables,
                intent.isAnswerable,
                intent.confidence,
            )
            return intent

        except Exception as ex:
            logger.warning(
                "Intent extraction failed, falling back to full schema: %s", ex
            )
            return None

    # ── Step 2: schema filtering ───────────────────────────────

    def filter_schema(
        self,
        full_schema: SchemaMetadata,
        intent: ExtractedIntent,
    ) -> SchemaMetadata:
        """
        Filter the full database schema to only the tables (and their
        relationships) that are relevant to the extracted intent.

        Filtering strategy (defense-in-depth):
          1. **Direct match** — tables named in ``intent.relevantTables``.
          2. **Relationship expansion** — tables connected via foreign-key
             relationships to any directly matched table.
          3. **Fallback** — if nothing matches, return the full schema
             so the SQL generation step still has context.

        Column-level filtering is intentionally conservative:
          - ID / foreign-key columns are always kept.
          - Columns named in ``intent.relevantColumns`` are kept.
          - If fewer than 2 columns survive, *all* columns are kept
            for that table (avoids over-aggressive pruning).
        """
        if not intent.relevantTables:
            logger.info("No relevant tables in intent — using full schema")
            return full_schema

        # Normalise for case-insensitive comparison
        intent_tables_lower = {t.lower() for t in intent.relevantTables}

        # ── direct match ──
        matched_tables: list[TableMetadata] = []
        matched_names: set[str] = set()

        for table in full_schema.tables:
            if table.tableName.lower() in intent_tables_lower:
                matched_tables.append(table)
                matched_names.add(table.tableName.lower())

        # ── relationship expansion ──
        if full_schema.relationships:
            related_names: set[str] = set()
            for rel in full_schema.relationships:
                if rel.fromTable.lower() in matched_names:
                    related_names.add(rel.toTable.lower())
                if rel.toTable.lower() in matched_names:
                    related_names.add(rel.fromTable.lower())

            for table in full_schema.tables:
                tname = table.tableName.lower()
                if tname in related_names and tname not in matched_names:
                    matched_tables.append(table)
                    matched_names.add(tname)

        # ── fallback ──
        if not matched_tables:
            logger.info(
                "No schema tables matched intent tables %s — using full schema",
                intent.relevantTables,
            )
            return full_schema

        # ── column-level filtering (conservative) ──
        if intent.relevantColumns:
            intent_cols_lower = {
                c.lower().split(".")[-1] for c in intent.relevantColumns
            }
            filtered_tables: list[TableMetadata] = []
            for table in matched_tables:
                relevant_cols = [
                    col
                    for col in table.columns
                    if col.columnName.lower() in intent_cols_lower
                    or col.columnName.lower() == "id"
                    or col.columnName.lower().endswith("_id")
                ]
                if len(relevant_cols) < 2:
                    # too aggressive — keep all columns
                    filtered_tables.append(table)
                else:
                    filtered_tables.append(
                        TableMetadata(
                            tableName=table.tableName,
                            description=table.description,
                            columns=relevant_cols,
                        )
                    )
            matched_tables = filtered_tables

        logger.info(
            "Schema filtered: %d/%d tables selected %s",
            len(matched_tables),
            len(full_schema.tables),
            [t.tableName for t in matched_tables],
        )

        # keep only relationships between matched tables
        filtered_rels = [
            rel
            for rel in full_schema.relationships
            if rel.fromTable.lower() in matched_names
            and rel.toTable.lower() in matched_names
        ]

        return SchemaMetadata(
            dialect=full_schema.dialect,
            tables=matched_tables,
            relationships=filtered_rels,
        )
