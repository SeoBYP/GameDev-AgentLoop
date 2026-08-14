// 배포/호출 검증용 최소 스크립트.
// 플레이어 식별자를 context 가 아니라 params 로 받는다 —
// 서비스 계정(trusted client)으로 호출해도 검증 가능하게 하기 위해서(UGS-INVOKE-DESIGN §6).
module.exports = async ({ params, context, logger }) => {
  const streak = Number(params.streak);

  if (!Number.isFinite(streak) || streak < 0) {
    return { granted: false, reason: "invalid streak" };
  }

  const cappedStreak = Math.min(streak, 7);
  const coins = cappedStreak * 100;

  logger.info("grantDailyReward streak=" + cappedStreak);
  return { granted: coins > 0, coins, streak: cappedStreak };
};

// 파라미터는 반드시 선언해야 스크립트로 전달된다.
// 선언하지 않으면 Cloud Code 가 걸러내서 params 가 비어 온다 — 배포는 성공하지만 동작은 틀린다.
// (module.exports 를 먼저 할당한 뒤에 params 를 붙여야 한다)
module.exports.params = {
  streak: { type: "Numeric", required: true },
};
