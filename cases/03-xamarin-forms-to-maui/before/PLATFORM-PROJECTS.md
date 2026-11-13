# Platform projects

`ParcelTracker.Android` and `ParcelTracker.iOS` contain the source files of the two Xamarin platform projects:
the entry points, the custom renderer for `BorderlessEntry`, and the per-platform services that the shared code
resolves through `DependencyService`.

Their project files are not included. Support for Xamarin ended on May 1, 2024, and the Xamarin toolchain is not
available on current build agents, so project files could not be built or verified. The shared project
`ParcelTracker`, which holds the pages, view models and API client, is built and tested in CI.
