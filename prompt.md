The current e2e tests in tests/Ratatoskr.Tests/Integration/ still use some mocks. I would prefer them to be as close to the real thing as possible. So try to remove as many mocks as possible and use the real implementations instead. Also try to use the public api of the project as much as possible. If it helps, use WebApplicationFactory to create the host for the tests. But tests should still run in parallel without interfering with each other. If there are opportunities to improve the public api to make it more testable, feel free to suggest and implement them. Also feel free to clean up or restructure the tests as you see fit.

----------------------------------------------------------

I refactored the projects configuration api and internals heavily and confirmed basic publishing and consuming to work manually via the example project. Now I want you to help me to get the project into a state where it is ready for release.

The first step for this is to improve the code quality and fix any issues. So please review the code and suggest any improvements. Focus on the following aspects:

- Code quality
- Performance
- Security
- Usability
- Testability
- Maintainability
- Documentation
- Examples
