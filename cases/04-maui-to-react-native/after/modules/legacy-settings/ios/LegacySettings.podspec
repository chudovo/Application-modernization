Pod::Spec.new do |s|
  s.name           = 'LegacySettings'
  s.version        = '1.0.0'
  s.summary        = 'Reads settings stored by the previous .NET MAUI version of the app'
  s.description    = 'Reads settings stored by the previous .NET MAUI version of the app'
  s.author         = 'Chudovo'
  s.homepage       = 'https://github.com/chudovo/application-modernization'
  s.license        = 'MIT'
  s.platforms      = { :ios => '15.1' }
  s.source         = { git: '' }
  s.static_framework = true
  s.dependency 'ExpoModulesCore'
  s.source_files = '**/*.{h,m,mm,swift}'
end
