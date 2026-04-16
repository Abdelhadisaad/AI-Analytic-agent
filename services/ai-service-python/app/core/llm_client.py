import asyncio
import json
from typing import Optional

import httpx

from app.config import settings
from app.core.exceptions import LlmProviderError, LlmTimeoutError
from app.core.prompting import build_system_prompt


class LlmClient:
    """
    OpenAI-compatible LLM client with support for Structured Outputs.

    Two generation modes:
      - ``generate()`` — legacy JSON-object mode (``response_format: json_object``).
      - ``generate_structured()`` — OpenAI Structured Outputs with a strict
        JSON schema (``response_format: json_schema``).  The API *guarantees*
        the output matches the schema, eliminating parse failures.

    Reference: https://platform.openai.com/docs/guides/structured-outputs
    """

    # ── legacy: json_object mode ──────────────────────────────

    async def generate(self, prompt: str) -> dict:
        headers = {
            "Authorization": f"Bearer {settings.llm_api_key}",
            "Content-Type": "application/json",
        }

        payload = {
            "model": settings.llm_model,
            "response_format": {"type": "json_object"},
            "messages": [
                {"role": "system", "content": build_system_prompt()},
                {"role": "user", "content": prompt},
            ],
            "temperature": 0.1,
        }

        return await self._call(headers, payload)

    # ── new: structured outputs mode ──────────────────────────

    async def generate_structured(
        self,
        prompt: str,
        system_prompt: str,
        json_schema: dict,
        temperature: float = 0.1,
    ) -> dict:
        """
        Call the LLM with OpenAI Structured Outputs (``strict: true``).

        Parameters
        ----------
        prompt : str
            The user-facing prompt.
        system_prompt : str
            System-level instructions for the LLM.
        json_schema : dict
            A dict with keys ``name``, ``strict``, and ``schema`` that
            defines the exact JSON structure the LLM must produce.
        temperature : float
            Sampling temperature (0.0 = deterministic).

        Returns
        -------
        dict
            The parsed JSON object guaranteed to match *json_schema*.
        """
        headers = {
            "Authorization": f"Bearer {settings.llm_api_key}",
            "Content-Type": "application/json",
        }

        payload = {
            "model": settings.llm_model,
            "response_format": {
                "type": "json_schema",
                "json_schema": json_schema,
            },
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": prompt},
            ],
            "temperature": temperature,
        }

        return await self._call(headers, payload)

    # ── shared HTTP transport ─────────────────────────────────

    async def _call(self, headers: dict, payload: dict) -> dict:
        timeout = httpx.Timeout(settings.llm_timeout_seconds)

        try:
            async with httpx.AsyncClient(timeout=timeout) as client:
                response = await asyncio.wait_for(
                    client.post(settings.llm_api_url, headers=headers, json=payload),
                    timeout=settings.llm_timeout_seconds,
                )

            response.raise_for_status()
            data = response.json()
            content = data["choices"][0]["message"]["content"]
            return json.loads(content)
        except asyncio.TimeoutError as ex:
            raise LlmTimeoutError("LLM call timed out.") from ex
        except httpx.TimeoutException as ex:
            raise LlmTimeoutError("LLM provider timeout.") from ex
        except (httpx.HTTPError, KeyError, ValueError, json.JSONDecodeError) as ex:
            raise LlmProviderError("LLM provider returned an invalid response.") from ex
