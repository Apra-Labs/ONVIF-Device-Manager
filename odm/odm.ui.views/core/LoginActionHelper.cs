namespace odm.ui.core
{
    public enum LoginAction { Case1SetAndRefresh, Case2RefreshWithStored, Case3Block }

    public static class LoginActionHelper
    {
        public static LoginAction Determine(string name, string pwd, int storedCount)
        {
            bool hasFields = !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(pwd);
            bool bothEmpty = string.IsNullOrEmpty(name) && string.IsNullOrEmpty(pwd);
            if (hasFields) return LoginAction.Case1SetAndRefresh;
            if (bothEmpty && storedCount > 0) return LoginAction.Case2RefreshWithStored;
            return LoginAction.Case3Block;
        }
    }
}
