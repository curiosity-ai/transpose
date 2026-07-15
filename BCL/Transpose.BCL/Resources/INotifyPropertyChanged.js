    Transpose.define("System.ComponentModel.INotifyPropertyChanged", {
        $kind: "interface"
    });

    Transpose.define("System.ComponentModel.PropertyChangedEventArgs", {
        ctor: function (propertyName, newValue, oldValue) {
            this.$initialize();
            this.propertyName = propertyName;
            this.newValue = newValue;
            this.oldValue = oldValue;
        }
    });
