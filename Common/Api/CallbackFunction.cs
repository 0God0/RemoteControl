namespace Common {

    #region �����ص�
    public delegate void UnlabeledExceptionCallback(Exception e);
    public delegate void ContextExceptionCallback(string text, Exception e);
    #endregion

    #region �����ص�ͨ��ʵ��
    static class CommonDefault {
        public static UnlabeledExceptionCallback dUnlabeledExceptionCallback = (Exception e) => { };
    }
    #endregion
}