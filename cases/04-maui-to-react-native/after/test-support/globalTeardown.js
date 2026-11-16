module.exports = async () => {
  globalThis.__TEST_BACKEND__?.kill();
};
