module.exports = async ({ params, context, logger }) => {
  const level = params.level;

  if (typeof level !== "number" || !Number.isInteger(level) || level < 1 || level > 100) {
    logger.warning(`Invalid level received: ${level}`);
    return {
      success: false,
      error: "level must be an integer between 1 and 100",
    };
  }

  const MAX_COINS = 3000;
  const coins = Math.min(level * 50, MAX_COINS);

  logger.info(`Level ${level} reward calculated: ${coins} coins`);

  return {
    success: true,
    level,
    coins,
  };
};

module.exports.params = {
  level: { type: "Numeric", required: true },
};