from app.schemas.generate_sql import GenerateSqlRequest


def build_system_prompt() -> str:
    return (
        "You are a PostgreSQL read-only SQL generator. "
        "You must follow all safety constraints strictly.\n"
        "Safety constraints:\n"
        "1) Only SELECT queries are allowed.\n"
        "2) Never generate schema/data modification commands (INSERT, UPDATE, DELETE, DROP, ALTER, TRUNCATE, CREATE, GRANT, REVOKE, COPY).\n"
        "3) Generate exactly one SQL statement (no multi-statement query, no semicolon chaining).\n"
        "4) Always include a LIMIT clause.\n"
        "5) Use only tables/columns provided in schema metadata.\n"
        "6) If the question cannot be answered safely from given schema, return a safe fallback SELECT with warnings.\n"
        "Output must be valid JSON only with this structure:\n"
        "{\n"
        "  \"sqlProposal\": {\n"
        "    \"dialect\": \"postgresql\",\n"
        "    \"sql\": \"...\",\n"
        "    \"parameters\": [{\"name\": \"...\", \"value\": null}]\n"
        "  },\n"
        "  \"explanationMetadata\": {\n"
        "    \"intentSummary\": \"...\",\n"
        "    \"reasoningSummary\": \"...\",\n"
        "    \"selectedTables\": [\"...\"],\n"
        "    \"selectedColumns\": [\"...\"],\n"
        "    \"assumptions\": [\"...\"],\n"
        "    \"confidenceScore\": 0.0,\n"
        "    \"warnings\": [\"...\"]\n"
        "  }\n"
        "}"
    )


def build_prompt(request: GenerateSqlRequest) -> str:
    table_lines = []
    for table in request.schemaMetadata.tables:
        columns = ", ".join([f"{column.columnName}:{column.dataType}" for column in table.columns])
        table_lines.append(f"- {table.tableName} ({columns})")

    relationships = []
    for relationship in request.schemaMetadata.relationships:
        relationships.append(
            f"- {relationship.fromTable}.{relationship.fromColumn} -> "
            f"{relationship.toTable}.{relationship.toColumn}"
        )

    tables_text = "\n".join(table_lines) if table_lines else "- none"
    relationships_text = "\n".join(relationships) if relationships else "- none"

    return (
        "Generate a read-only PostgreSQL query proposal for this request.\n"
        f"Natural language question: {request.naturalLanguageQuery}\n"
        "Use ONLY this schema metadata:\n"
        f"Schema tables:\n{tables_text}\n"
        f"Schema relationships:\n{relationships_text}\n"
        "Return JSON only. Do not include markdown, code fences, or extra text."
    )
