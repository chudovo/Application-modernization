import { configure } from '@testing-library/react-native';

// The screens load over real HTTP. One second, the default, is not always enough on a busy build agent.
configure({ asyncUtilTimeout: 10000 });
