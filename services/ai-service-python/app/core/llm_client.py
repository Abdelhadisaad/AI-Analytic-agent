import asyncio
import json

import httpx

from app.config import settings
from app.core.exceptions import LlmProviderError, LlmTimeoutError


class LlmClient:
    async def generate(self, prompt: str) -> dict:
        headers = {
            "Authorization": f"Bearer {settings.llm_api_key}",
            "Content-Type": "application/json",
        }

        payload = {
            "model": settings.llm_model,
            "response_format": {"type": "json_object"},
            "messages": [
                {"role": "system", "content": "You generate safe SQL proposals and explanations."},
                {"role": "user", "content": prompt},
            ],
            "temperature": 0.1,
        }

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
