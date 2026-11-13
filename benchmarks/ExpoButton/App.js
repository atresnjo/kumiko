import { useState } from 'react';
import { Button, StyleSheet, View } from 'react-native';

export default function App() {
  const [count, setCount] = useState(0);

  return (
    <View style={styles.root}>
      <Button
        title={`nice ${count} yes`}
        onPress={() => setCount(value => value + 1)}
        testID="counter"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: '#fff',
  },
});
