namespace Common {

    #region 公共回调
    public delegate void UnlabeledExceptionCallback(Exception e);
    public delegate void ContextExceptionCallback(string text, Exception e);
    #endregion

    #region 公共回调通用实现
    static class CommonDefault {
        public static UnlabeledExceptionCallback dUnlabeledExceptionCallback = (Exception e) => { };
    }
    #endregion
}