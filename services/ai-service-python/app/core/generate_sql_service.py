"""SQL Generation Service — two-step intent-first pipeline.

Pipeline:
  1. Intent extraction   (lightweight LLM call → ExtractedIntent)
  2. Schema filtering     (deterministic, no LLM)
  3. SQL generation       (focused LLM call with filtered schema + intent context)

If intent extraction fails, the service falls back to the original
single-call behaviour (full schema, no intent context) so the pipeline
never breaks.
"""

import logging
from typing import Optional

from app.core.intent_service import IntentService
from app.core.llm_client import LlmClient
from app.core.prompting import build_prompt, build_system_prompt
from app.schemas.generate_sql import GenerateSqlRequest, GenerateSqlResponse
from app.schemas.intent import ExtractedIntent, SQL_GENERATION_SCHEMA

logger = logging.getLogger(__name__)


class GenerateSqlService:
    def __init__(
        self,
        llm_client: LlmClient,
        intent_service: Optional[IntentService] = None,
    ) -> None:
        self._llm_client = llm_client
        self._intent_service = intent_service

    async def execute(self, request: GenerateSqlRequest) -> GenerateSqlResponse:
        intent: Optional[ExtractedIntent] = None
        filtered_schema = None

        # ── Step 1 + 2: intent extraction → schema filtering ──
        if self._intent_service is not None:
            intent = await self._intent_service.extract_intent(
                question=request.naturalLanguageQuery,
                schema=request.schemaMetadata,
            )
            if intent is not None:
                filtered_schema = self._intent_service.filter_schema(
                    full_schema=request.schemaMetadata,
                    intent=intent,
                )
                logger.info(
                    "Using filtered schema (%d tables) with intent type=%s",
                    len(filtered_schema.tables),
                    intent.queryType,
                )

        # ── Step 3: SQL generation with structured outputs ────
        prompt = build_prompt(
            request,
            intent=intent,
            filtered_schema=filtered_schema,
        )
        system_prompt = build_system_prompt()

        llm_json = await self._llm_client.generate_structured(
            prompt=prompt,
            system_prompt=system_prompt,
            json_schema=SQL_GENERATION_SCHEMA,
            temperature=0.1,
        )

        # ── assemble response ─────────────────────────────────
        llm_json["requestId"] = request.requestId
        llm_json["correlationId"] = request.correlationId

        response = GenerateSqlResponse.model_validate(llm_json)

        # Attach extracted intent for observability
        if intent is not None:
            response.extractedIntent = intent

        return response
