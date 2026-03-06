from app.schemas.generate_sql import GenerateSqlRequest


def build_prompt(request: GenerateSqlRequest) -> str:
    table_lines = []
    for table in request.schemaMetadata.tables:
        columns = ", ".join([f"{column.name}:{column.type}" for column in table.columns])
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
        "You are a SQL assistant for PostgreSQL. Return strict JSON with keys: "
        "sqlProposal and explanationMetadata.\n"
        f"User question: {request.naturalLanguageQuery}\n"
        f"Schema tables:\n{tables_text}\n"
        f"Schema relationships:\n{relationships_text}\n"
        "Rules: read-only SELECT only, include LIMIT.")
