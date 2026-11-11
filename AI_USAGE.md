# AI usage

Claude, Copilot, and GPT helped implement and review the API, database concurrency, Docker setup, React dashboard, translations, and automated tests. I made the product decisions and reviewed the assumptions, library choices, and architecture with AI. After using AI to generate the boilerplate and initial implementation, I modified the code myself.

| Issue or proposal | How it was caught and corrected |
|---|---|
| HTTP polling was proposed for periodic seat refreshes. I chose SSE so the server can push seat changes when they occur. | I reviewed the live-update design. The implementation uses SSE over HTTP with PostgreSQL notifications so both backend processes can learn about a change. |
| AI initially recommended changing the application rule to per class without carrying forward my academic-year assumption. | I stand with decision that one student should only have one active application in the same year. The session default and waitlist status were retained and documented explicitly. |
| The first isolated test run exhausted PostgreSQL connections when the test host and both API containers ran together. | PostgreSQL reported too many clients. I bounded each pool in the test Compose file and reran the suite. |
| The first waitlist test expected two queue entries for a class with only one held seat. | The test returned a conflict for the second entry. I checked the documented queue limit, set up two held seats, and added an assertion that a third queue entry is rejected. |
